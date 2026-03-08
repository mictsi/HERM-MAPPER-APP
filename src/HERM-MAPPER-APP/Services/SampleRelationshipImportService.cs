using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.RegularExpressions;
using HERM_MAPPER_APP.Data;
using HERM_MAPPER_APP.Models;
using Microsoft.EntityFrameworkCore;

namespace HERM_MAPPER_APP.Services;

public sealed partial class SampleRelationshipImportService(AppDbContext dbContext)
{
    public async Task<ProductRelationshipVerificationResult> VerifyAsync(string csvPath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(csvPath))
        {
            return new ProductRelationshipVerificationResult
            {
                Errors = ["The selected CSV file could not be found."]
            };
        }

        await using var stream = File.OpenRead(csvPath);
        return await VerifyAsync(stream, cancellationToken);
    }

    public async Task<ProductRelationshipVerificationResult> VerifyAsync(Stream csvStream, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(csvStream);

        using var reader = new StreamReader(csvStream, leaveOpen: true);

        var headerLine = await reader.ReadLineAsync();
        if (string.IsNullOrWhiteSpace(headerLine))
        {
            return new ProductRelationshipVerificationResult
            {
                Errors = ["The CSV file is empty."]
            };
        }

        var headerParts = headerLine.Split(';');
        if (headerParts.Length < 5 ||
            !string.Equals(headerParts[0].Trim(), "MODEL", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(headerParts[1].Trim(), "DOMAIN", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(headerParts[2].Trim(), "CAPABILITY", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(headerParts[3].Trim(), "COMPONENT", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(headerParts[4].Trim(), "PRODUCT", StringComparison.OrdinalIgnoreCase))
        {
            return new ProductRelationshipVerificationResult
            {
                Errors = ["The CSV header must be 'MODEL;DOMAIN;CAPABILITY;COMPONENT;PRODUCT'."]
            };
        }

        var domains = await dbContext.TrmDomains
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var capabilities = await dbContext.TrmCapabilities
            .AsNoTracking()
            .Include(x => x.ParentDomain)
            .ToListAsync(cancellationToken);

        var components = await dbContext.TrmComponents
            .AsNoTracking()
            .Include(x => x.ParentCapability)
            .ThenInclude(x => x!.ParentDomain)
            .ToListAsync(cancellationToken);

        var existingProductNames = await dbContext.ProductCatalogItems
            .AsNoTracking()
            .Select(x => x.Name)
            .ToListAsync(cancellationToken);

        var knownProductNames = existingProductNames
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var matchedExistingProductNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var existingMappings = (await dbContext.ProductMappings
                .AsNoTracking()
                .Include(x => x.ProductCatalogItem)
                .Select(x => new
                {
                    ProductName = x.ProductCatalogItem != null ? x.ProductCatalogItem.Name : string.Empty,
                    x.TrmDomainId,
                    x.TrmCapabilityId,
                    x.TrmComponentId
                })
                .ToListAsync(cancellationToken))
            .Select(x => BuildMappingKey(x.ProductName, x.TrmDomainId, x.TrmCapabilityId, x.TrmComponentId))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var pendingMappings = new HashSet<string>(existingMappings, StringComparer.OrdinalIgnoreCase);
        var summary = new ProductRelationshipVerificationResult();
        var rows = new List<ProductRelationshipVerificationRow>();
        var rowNumber = 1;

        while (await reader.ReadLineAsync() is { } line)
        {
            cancellationToken.ThrowIfCancellationRequested();
            rowNumber++;

            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            summary.RowsRead++;

            var parts = line.Split(';');
            if (parts.Length < 5)
            {
                summary.RowsSkipped++;
                rows.Add(new ProductRelationshipVerificationRow
                {
                    RowNumber = rowNumber,
                    Status = "Skipped",
                    StatusKind = ProductImportStatusKind.Skipped,
                    Details = "The row does not contain the required five columns.",
                    WillImport = false
                });
                continue;
            }

            var modelName = parts[0].Trim();
            var domainRaw = parts[1].Trim();
            var capabilityRaw = parts[2].Trim();
            var componentRaw = parts[3].Trim();
            var productName = parts[4].Trim();

            if (string.IsNullOrWhiteSpace(productName))
            {
                summary.RowsSkipped++;
                rows.Add(new ProductRelationshipVerificationRow
                {
                    RowNumber = rowNumber,
                    ModelName = modelName,
                    DomainName = domainRaw,
                    CapabilityName = capabilityRaw,
                    ComponentName = componentRaw,
                    Status = "Skipped",
                    StatusKind = ProductImportStatusKind.Skipped,
                    Details = "PRODUCT is empty, so no product can be imported.",
                    WillImport = false
                });
                continue;
            }

            var productAlreadyKnown = knownProductNames.Contains(productName);
            var createsNewProduct = !productAlreadyKnown;
            if (createsNewProduct)
            {
                knownProductNames.Add(productName);
                summary.ProductsToAdd++;
            }
            else if (matchedExistingProductNames.Add(productName) && existingProductNames.Contains(productName, StringComparer.OrdinalIgnoreCase))
            {
                summary.ProductsMatched++;
            }

            if (!TryResolveValidatedMapping(
                    modelName,
                    domainRaw,
                    capabilityRaw,
                    componentRaw,
                    domains,
                    capabilities,
                    components,
                    out var resolvedMapping,
                    out var resolutionMessage))
            {
                summary.ProductsOnlyRows++;
                rows.Add(new ProductRelationshipVerificationRow
                {
                    RowNumber = rowNumber,
                    ModelName = modelName,
                    DomainName = domainRaw,
                    CapabilityName = capabilityRaw,
                    ComponentName = componentRaw,
                    ProductName = productName,
                    Status = createsNewProduct ? "Add Product Only" : "Keep Product Only",
                    StatusKind = ProductImportStatusKind.Warning,
                    Details = resolutionMessage,
                    WillCreateProduct = createsNewProduct,
                    WillCreateMapping = false,
                    WillImport = true
                });
                continue;
            }

            var mappingKey = BuildMappingKey(productName, resolvedMapping.Domain.Id, resolvedMapping.Capability.Id, resolvedMapping.Component.Id);
            if (!pendingMappings.Add(mappingKey))
            {
                summary.MappingsSkippedAsDuplicate++;
                rows.Add(new ProductRelationshipVerificationRow
                {
                    RowNumber = rowNumber,
                    ModelName = modelName,
                    DomainName = domainRaw,
                    CapabilityName = capabilityRaw,
                    ComponentName = componentRaw,
                    ProductName = productName,
                    Status = "Skip Duplicate Mapping",
                    StatusKind = ProductImportStatusKind.Skipped,
                    Details = "That product-to-component mapping already exists.",
                    WillCreateProduct = createsNewProduct,
                    WillCreateMapping = false,
                    WillImport = createsNewProduct
                });
                continue;
            }

            summary.MappingsToAdd++;
            rows.Add(new ProductRelationshipVerificationRow
            {
                RowNumber = rowNumber,
                ModelName = modelName,
                DomainName = domainRaw,
                CapabilityName = capabilityRaw,
                ComponentName = componentRaw,
                ProductName = productName,
                Status = createsNewProduct ? "Add Product + Mapping" : "Add Mapping",
                StatusKind = ProductImportStatusKind.Ready,
                Details = resolutionMessage,
                WillCreateProduct = createsNewProduct,
                WillCreateMapping = true,
                WillImport = true
            });
        }

        summary.Rows = rows;
        return summary;
    }

    public async Task<ProductRelationshipImportSummary> ImportAsync(string csvPath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(csvPath))
        {
            return ProductRelationshipImportSummary.Empty;
        }

        await using var stream = File.OpenRead(csvPath);
        return await ImportAsync(stream, cancellationToken);
    }

    public async Task<ProductRelationshipImportSummary> ImportAsync(Stream csvStream, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(csvStream);

        using var reader = new StreamReader(csvStream, leaveOpen: true);
        var headerLine = await reader.ReadLineAsync();
        if (!HasValidHeader(headerLine))
        {
            return ProductRelationshipImportSummary.Empty;
        }

        var domains = await dbContext.TrmDomains
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var capabilities = await dbContext.TrmCapabilities
            .AsNoTracking()
            .Include(x => x.ParentDomain)
            .ToListAsync(cancellationToken);

        var components = await dbContext.TrmComponents
            .AsNoTracking()
            .Include(x => x.ParentCapability)
            .ThenInclude(x => x!.ParentDomain)
            .ToListAsync(cancellationToken);

        var products = await dbContext.ProductCatalogItems
            .OrderBy(x => x.Id)
            .ToListAsync(cancellationToken);

        var productLookup = products
            .GroupBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var existingProductNames = products
            .Select(x => x.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var matchedExistingProductNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var importedMappings = (await dbContext.ProductMappings
                .AsNoTracking()
                .Include(x => x.ProductCatalogItem)
                .Select(x => new
                {
                    ProductName = x.ProductCatalogItem != null ? x.ProductCatalogItem.Name : string.Empty,
                    x.TrmDomainId,
                    x.TrmCapabilityId,
                    x.TrmComponentId
                })
                .ToListAsync(cancellationToken))
            .Select(x => BuildMappingKey(x.ProductName, x.TrmDomainId, x.TrmCapabilityId, x.TrmComponentId))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var summary = new ProductRelationshipImportSummary();

        while (await reader.ReadLineAsync() is { } line)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            summary.RowsRead++;

            var parts = line.Split(';');
            if (parts.Length < 5)
            {
                summary.RowsSkipped++;
                continue;
            }

            var modelName = parts[0].Trim();
            var domainRaw = parts[1].Trim();
            var capabilityRaw = parts[2].Trim();
            var componentRaw = parts[3].Trim();
            var productName = parts[4].Trim();

            if (string.IsNullOrWhiteSpace(productName))
            {
                summary.RowsSkipped++;
                continue;
            }

            if (!productLookup.TryGetValue(productName, out var product))
            {
                product = new ProductCatalogItem
                {
                    Name = productName,
                    Notes = "Imported from sample relationship CSV.",
                    CreatedUtc = DateTime.UtcNow,
                    UpdatedUtc = DateTime.UtcNow
                };

                productLookup[productName] = product;
                dbContext.ProductCatalogItems.Add(product);
                summary.ProductsAdded++;
            }
            else
            {
                if (existingProductNames.Contains(product.Name) && matchedExistingProductNames.Add(product.Name))
                {
                    summary.ProductsMatched++;
                }
            }

            if (!TryResolveValidatedMapping(
                    modelName,
                    domainRaw,
                    capabilityRaw,
                    componentRaw,
                    domains,
                    capabilities,
                    components,
                    out var resolvedMapping,
                    out _))
            {
                summary.ProductsOnlyRows++;
                continue;
            }

            var mappingKey = BuildMappingKey(product.Name, resolvedMapping.Domain.Id, resolvedMapping.Capability.Id, resolvedMapping.Component.Id);
            if (!importedMappings.Add(mappingKey))
            {
                summary.MappingsSkippedAsDuplicate++;
                continue;
            }

            dbContext.ProductMappings.Add(new ProductMapping
            {
                ProductCatalogItem = product,
                TrmDomainId = resolvedMapping.Domain.Id,
                TrmCapabilityId = resolvedMapping.Capability.Id,
                TrmComponentId = resolvedMapping.Component.Id,
                MappingStatus = MappingStatus.Complete,
                MappingRationale = "Imported from sample relationship CSV.",
                LastReviewedUtc = DateTime.UtcNow,
                CreatedUtc = DateTime.UtcNow,
                UpdatedUtc = DateTime.UtcNow
            });
            summary.MappingsAdded++;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return summary;
    }

    private static bool TryResolveValidatedMapping(
        string modelName,
        string domainRaw,
        string capabilityRaw,
        string componentRaw,
        IReadOnlyCollection<TrmDomain> domains,
        IReadOnlyCollection<TrmCapability> capabilities,
        IReadOnlyCollection<TrmComponent> components,
        [NotNullWhen(true)] out ResolvedRelationshipMapping? resolvedMapping,
        out string resolutionMessage)
    {
        resolvedMapping = default;
        resolutionMessage = string.Empty;

        if (!string.IsNullOrWhiteSpace(modelName) &&
            !string.Equals(modelName, "HERM", StringComparison.OrdinalIgnoreCase))
        {
            resolutionMessage = $"MODEL '{modelName}' is not supported. Expected 'HERM'.";
            return false;
        }

        var domain = ResolveDomain(domainRaw, domains);
        if (domain is null)
        {
            resolutionMessage = $"DOMAIN '{domainRaw}' could not be matched to a unique TRM domain.";
            return false;
        }

        var capability = ResolveCapability(capabilityRaw, domain.Id, capabilities);
        if (capability is null)
        {
            resolutionMessage = $"CAPABILITY '{capabilityRaw}' could not be matched under domain '{domain.Code} {domain.Name}'.";
            return false;
        }

        var component = ResolveComponent(componentRaw, capability.Id, components);
        if (component is null)
        {
            resolutionMessage = $"COMPONENT '{componentRaw}' could not be matched under capability '{capability.Code} {capability.Name}'.";
            return false;
        }

        resolvedMapping = new ResolvedRelationshipMapping(domain, capability, component);
        resolutionMessage = $"Maps to {domain.Code} {domain.Name} / {capability.Code} {capability.Name} / {component.DisplayLabel}.";
        return true;
    }

    private static bool HasValidHeader(string? headerLine)
    {
        if (string.IsNullOrWhiteSpace(headerLine))
        {
            return false;
        }

        var headerParts = headerLine.Split(';');
        return headerParts.Length >= 5 &&
               string.Equals(headerParts[0].Trim(), "MODEL", StringComparison.OrdinalIgnoreCase) &&
               string.Equals(headerParts[1].Trim(), "DOMAIN", StringComparison.OrdinalIgnoreCase) &&
               string.Equals(headerParts[2].Trim(), "CAPABILITY", StringComparison.OrdinalIgnoreCase) &&
               string.Equals(headerParts[3].Trim(), "COMPONENT", StringComparison.OrdinalIgnoreCase) &&
               string.Equals(headerParts[4].Trim(), "PRODUCT", StringComparison.OrdinalIgnoreCase);
    }

    private static TrmDomain? ResolveDomain(string rawValue, IReadOnlyCollection<TrmDomain> domains) =>
        ResolveByCodeNameOrTitle(rawValue, domains, x => x.Code, x => x.Name, x => x.SourceTitle);

    private static TrmCapability? ResolveCapability(string rawValue, int domainId, IReadOnlyCollection<TrmCapability> capabilities) =>
        ResolveByCodeNameOrTitle(
            rawValue,
            capabilities.Where(x => x.ParentDomainId == domainId).ToList(),
            x => x.Code,
            x => x.Name,
            x => x.SourceTitle);

    private static TrmComponent? ResolveComponent(string rawValue, int capabilityId, IReadOnlyCollection<TrmComponent> components) =>
        ResolveByCodeNameOrTitle(
            rawValue,
            components.Where(x => x.ParentCapabilityId == capabilityId).ToList(),
            x => x.Code,
            x => x.Name,
            x => x.SourceTitle);

    private static T? ResolveByCodeNameOrTitle<T>(
        string rawValue,
        IReadOnlyCollection<T> items,
        Func<T, string?> codeSelector,
        Func<T, string?> nameSelector,
        Func<T, string?> titleSelector)
        where T : class
    {
        var parsedValue = ParseLookupValue(rawValue);
        if (string.IsNullOrWhiteSpace(parsedValue.Code) && string.IsNullOrWhiteSpace(parsedValue.Label))
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(parsedValue.Code))
        {
            var codeMatch = items.FirstOrDefault(x =>
                string.Equals(NormalizeCode(codeSelector(x)), parsedValue.Code, StringComparison.OrdinalIgnoreCase));

            if (codeMatch is null)
            {
                return null;
            }

            if (!string.IsNullOrWhiteSpace(parsedValue.Label) && !MatchesLabel(parsedValue.Label, nameSelector(codeMatch), titleSelector(codeMatch)))
            {
                return null;
            }

            return codeMatch;
        }

        var labelMatches = items
            .Where(x => MatchesLabel(parsedValue.Label, nameSelector(x), titleSelector(x)))
            .ToList();

        return labelMatches.Count == 1 ? labelMatches[0] : null;
    }

    private static bool MatchesLabel(string? expectedLabel, string? name, string? sourceTitle)
    {
        if (string.IsNullOrWhiteSpace(expectedLabel))
        {
            return false;
        }

        return string.Equals(name, expectedLabel, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(sourceTitle, expectedLabel, StringComparison.OrdinalIgnoreCase);
    }

    private static LookupValue ParseLookupValue(string rawValue)
    {
        var trimmedValue = rawValue.Trim();
        if (string.IsNullOrWhiteSpace(trimmedValue))
        {
            return new LookupValue(null, string.Empty);
        }

        var legacyCode = ExtractComponentCode(trimmedValue);
        if (!string.IsNullOrWhiteSpace(legacyCode))
        {
            return new LookupValue(NormalizeCode(legacyCode), ExtractComponentName(trimmedValue));
        }

        var leadingCodeMatch = LeadingCodeAndNameRegex().Match(trimmedValue);
        if (leadingCodeMatch.Success)
        {
            return new LookupValue(
                NormalizeCode(leadingCodeMatch.Groups["code"].Value),
                leadingCodeMatch.Groups["name"].Value.Trim());
        }

        return new LookupValue(null, trimmedValue);
    }

    private static string BuildMappingKey(string productName, int? domainId, int? capabilityId, int? componentId) =>
        $"{productName.Trim()}|{domainId?.ToString() ?? "-"}|{capabilityId?.ToString() ?? "-"}|{componentId?.ToString() ?? "-"}";

    private static string ExtractComponentName(string rawValue)
    {
        var match = ComponentCodeRegex().Match(rawValue);
        return match.Success ? rawValue[..match.Index].Trim() : rawValue.Trim();
    }

    private static string? ExtractComponentCode(string rawValue)
    {
        var match = ComponentCodeRegex().Match(rawValue);
        return match.Success ? match.Groups["code"].Value : null;
    }

    private static string? NormalizeCode(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return null;
        }

        var match = GenericCodeRegex().Match(code);
        if (!match.Success)
        {
            return code.Trim();
        }

        var prefix = match.Groups["prefix"].Value;
        var number = int.Parse(match.Groups["number"].Value, CultureInfo.InvariantCulture);
        return $"{prefix}{number:000}";
    }

    [GeneratedRegex(@"\((?<code>TC\d+)\)", RegexOptions.CultureInvariant)]
    private static partial Regex ComponentCodeRegex();

    [GeneratedRegex(@"^(?<code>[A-Z]+\d+)\s+(?<name>.+)$", RegexOptions.CultureInvariant)]
    private static partial Regex LeadingCodeAndNameRegex();

    [GeneratedRegex(@"^(?<prefix>[A-Z]+)(?<number>\d+)$", RegexOptions.CultureInvariant)]
    private static partial Regex GenericCodeRegex();

    private sealed record ResolvedRelationshipMapping(TrmDomain Domain, TrmCapability Capability, TrmComponent Component);
    private sealed record LookupValue(string? Code, string Label);
}

public sealed class ProductRelationshipImportSummary
{
    public static ProductRelationshipImportSummary Empty { get; } = new();

    public int RowsRead { get; set; }
    public int RowsSkipped { get; set; }
    public int ProductsAdded { get; set; }
    public int ProductsMatched { get; set; }
    public int ProductsOnlyRows { get; set; }
    public int MappingsAdded { get; set; }
    public int MappingsSkippedAsDuplicate { get; set; }
}

public sealed class ProductRelationshipVerificationResult
{
    public bool IsValid => Errors.Count == 0;
    public int RowsRead { get; set; }
    public int RowsSkipped { get; set; }
    public int ProductsToAdd { get; set; }
    public int ProductsMatched { get; set; }
    public int ProductsOnlyRows { get; set; }
    public int MappingsToAdd { get; set; }
    public int MappingsSkippedAsDuplicate { get; set; }
    public IReadOnlyList<string> Errors { get; init; } = [];
    public IReadOnlyList<string> Warnings { get; init; } = [];
    public IReadOnlyList<ProductRelationshipVerificationRow> Rows { get; set; } = [];
}

public sealed class ProductRelationshipVerificationRow
{
    public int RowNumber { get; init; }
    public string? ModelName { get; init; }
    public string? DomainName { get; init; }
    public string? CapabilityName { get; init; }
    public string? ComponentName { get; init; }
    public string? ProductName { get; init; }
    public string Status { get; init; } = string.Empty;
    public ProductImportStatusKind StatusKind { get; init; }
    public string Details { get; init; } = string.Empty;
    public bool WillCreateProduct { get; init; }
    public bool WillCreateMapping { get; init; }
    public bool WillImport { get; init; }
}

public enum ProductImportStatusKind
{
    Ready,
    Warning,
    Skipped
}
