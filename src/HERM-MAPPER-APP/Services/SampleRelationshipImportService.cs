using System.Globalization;
using System.Text.RegularExpressions;
using HERM_MAPPER_APP.Data;
using HERM_MAPPER_APP.Models;
using Microsoft.EntityFrameworkCore;

namespace HERM_MAPPER_APP.Services;

public sealed partial class SampleRelationshipImportService(AppDbContext dbContext)
{
    public async Task ImportAsync(string csvPath, CancellationToken cancellationToken = default)
    {
        if (await dbContext.ProductCatalogItems.AnyAsync(cancellationToken) || !File.Exists(csvPath))
        {
            return;
        }

        var components = await dbContext.TrmComponents
            .AsNoTracking()
            .Include(x => x.ParentCapability)
            .ThenInclude(x => x!.ParentDomain)
            .ToListAsync(cancellationToken);

        var domains = await dbContext.TrmDomains.AsNoTracking().ToListAsync(cancellationToken);
        var productLookup = new Dictionary<string, ProductCatalogItem>(StringComparer.OrdinalIgnoreCase);

        foreach (var line in File.ReadLines(csvPath).Skip(1))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var parts = line.Split(';');
            if (parts.Length < 4)
            {
                continue;
            }

            var domainName = parts[1].Trim();
            var componentRaw = parts[2].Trim();
            var productName = parts[3].Trim();

            if (string.IsNullOrWhiteSpace(productName))
            {
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
            }

            var componentCode = ExtractComponentCode(componentRaw);
            var componentName = ExtractComponentName(componentRaw);
            var normalizedCode = NormalizeCode(componentCode);

            var component = components.FirstOrDefault(x =>
                (!string.IsNullOrWhiteSpace(normalizedCode) && string.Equals(x.Code, normalizedCode, StringComparison.OrdinalIgnoreCase)) ||
                string.Equals(x.Name, componentName, StringComparison.OrdinalIgnoreCase));

            var domain = domains.FirstOrDefault(x => string.Equals(x.Name, domainName, StringComparison.OrdinalIgnoreCase))
                ?? component?.ParentCapability?.ParentDomain;

            dbContext.ProductMappings.Add(new ProductMapping
            {
                ProductCatalogItem = product,
                TrmDomainId = domain?.Id,
                TrmCapabilityId = component?.ParentCapabilityId,
                TrmComponentId = component?.Id,
                MappingStatus = MappingStatus.Complete,
                MappingRationale = "Imported from sample relationship CSV.",
                LastReviewedUtc = DateTime.UtcNow,
                CreatedUtc = DateTime.UtcNow,
                UpdatedUtc = DateTime.UtcNow
            });
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

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
}
