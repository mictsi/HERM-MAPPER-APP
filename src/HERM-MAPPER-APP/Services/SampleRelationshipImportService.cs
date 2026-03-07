using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.RegularExpressions;
using HERM_MAPPER_APP.Data;
using HERM_MAPPER_APP.Models;
using Microsoft.EntityFrameworkCore;

namespace HERM_MAPPER_APP.Services;

public sealed partial class SampleRelationshipImportService(AppDbContext dbContext)
{
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
        var isHeaderRow = true;

        while (await reader.ReadLineAsync() is { } line)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (isHeaderRow)
            {
                isHeaderRow = false;
                continue;
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            summary.RowsRead++;

            var parts = line.Split(';');
            if (parts.Length < 4)
            {
                summary.RowsSkipped++;
                continue;
            }

            var level0Name = parts[0].Trim();
            var domainName = parts[1].Trim();
            var componentRaw = parts[2].Trim();
            var productName = parts[3].Trim();

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

            if (!TryResolveValidatedMapping(level0Name, domainName, componentRaw, components, out var resolvedMapping))
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
        string level0Name,
        string domainName,
        string componentRaw,
        IReadOnlyCollection<TrmComponent> components,
        [NotNullWhen(true)] out ResolvedRelationshipMapping? resolvedMapping)
    {
        resolvedMapping = default;

        if (!string.IsNullOrWhiteSpace(level0Name) &&
            !string.Equals(level0Name, "HERM", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var component = ResolveComponent(componentRaw, components);
        var capability = component?.ParentCapability;
        var domain = capability?.ParentDomain;

        if (component is null || capability is null || domain is null)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(domainName) ||
            !string.Equals(domain.Name, domainName, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        resolvedMapping = new ResolvedRelationshipMapping(domain, capability, component);
        return true;
    }

    private static TrmComponent? ResolveComponent(string rawValue, IReadOnlyCollection<TrmComponent> components)
    {
        var componentCode = ExtractComponentCode(rawValue);
        var componentName = ExtractComponentName(rawValue);
        var normalizedCode = NormalizeCode(componentCode);

        if (!string.IsNullOrWhiteSpace(normalizedCode))
        {
            var codeMatch = components.FirstOrDefault(x =>
                string.Equals(x.Code, normalizedCode, StringComparison.OrdinalIgnoreCase));

            if (codeMatch is not null)
            {
                return codeMatch;
            }
        }

        var nameMatches = components
            .Where(x => string.Equals(x.Name, componentName, StringComparison.OrdinalIgnoreCase))
            .ToList();

        return nameMatches.Count == 1 ? nameMatches[0] : null;
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

    [GeneratedRegex(@"^(?<prefix>[A-Z]+)(?<number>\d+)$", RegexOptions.CultureInvariant)]
    private static partial Regex GenericCodeRegex();

    private sealed record ResolvedRelationshipMapping(TrmDomain Domain, TrmCapability Capability, TrmComponent Component);
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
