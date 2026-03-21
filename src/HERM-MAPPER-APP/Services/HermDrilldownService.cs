using HERMMapperApp.Data;
using HERMMapperApp.Models;
using HERMMapperApp.ViewModels;
using Microsoft.EntityFrameworkCore;

namespace HERMMapperApp.Services;

public sealed class HermDrilldownService(AppDbContext dbContext)
{
    public async Task<ApplicationDetailsViewModel?> BuildApplicationDetailsAsync(int applicationId, CancellationToken cancellationToken = default)
    {
        var application = await dbContext.ApplicationCatalogItems
            .AsNoTracking()
            .Include(x => x.Mappings)
            .ThenInclude(x => x.ArmComponent)
            .ThenInclude(x => x!.ParentCapability)
            .ThenInclude(x => x!.ParentDomain)
            .Include(x => x.Mappings)
            .ThenInclude(x => x.ProductCatalogItem)
            .ThenInclude(x => x!.Mappings)
            .ThenInclude(x => x.TrmDomain)
            .Include(x => x.Mappings)
            .ThenInclude(x => x.ProductCatalogItem)
            .ThenInclude(x => x!.Mappings)
            .ThenInclude(x => x.TrmCapability)
            .ThenInclude(x => x!.ParentDomain)
            .Include(x => x.Mappings)
            .ThenInclude(x => x.ProductCatalogItem)
            .ThenInclude(x => x!.Mappings)
            .ThenInclude(x => x.TrmComponent)
            .ThenInclude(x => x!.ParentCapability)
            .ThenInclude(x => x!.ParentDomain)
            .AsSplitQuery()
            .FirstOrDefaultAsync(x => x.Id == applicationId, cancellationToken);

        if (application is null)
        {
            return null;
        }

        var mappingRows = application.Mappings
            .OrderByDescending(x => x.IsPrimary)
            .ThenBy(x => x.ArmComponent!.Code)
            .ThenBy(x => x.ProductCatalogItem!.Name)
            .Select(mapping => new ApplicationMappingRowViewModel
            {
                ArmDomainLabel = BuildArmDomainLabel(mapping.ArmComponent),
                ArmCapabilityLabel = BuildArmCapabilityLabel(mapping.ArmComponent),
                ArmComponentLabel = mapping.ArmComponent?.DisplayLabel ?? "-",
                ProductLabel = BuildProductLabel(mapping.ProductCatalogItem),
                IsPrimary = mapping.IsPrimary,
                Notes = mapping.Notes,
                ResolvedTrmPathCount = mapping.ProductCatalogItem?.Mappings.Count ?? 0
            })
            .ToList();

        var resolvedPaths = application.Mappings
            .SelectMany(BuildApplicationPaths)
            .OrderBy(x => x.ArmDomainLabel)
            .ThenBy(x => x.ArmCapabilityLabel)
            .ThenBy(x => x.ArmComponentLabel)
            .ThenBy(x => x.ProductLabel)
            .ThenBy(x => x.TrmDomainLabel)
            .ThenBy(x => x.TrmCapabilityLabel)
            .ThenBy(x => x.TrmComponentLabel)
            .ToList();

        return new ApplicationDetailsViewModel
        {
            Id = application.Id,
            Name = application.Name,
            Description = application.Description,
            Notes = application.Notes,
            UpdatedUtc = application.UpdatedUtc,
            MappingRows = mappingRows,
            ResolvedPaths = resolvedPaths,
            ArmComponentCount = application.Mappings
                .Select(x => x.ArmComponentId)
                .Distinct()
                .Count(),
            ProductCount = application.Mappings
                .Select(x => x.ProductCatalogItemId)
                .Distinct()
                .Count()
        };
    }

    public async Task<CapabilityDetailsViewModel?> BuildCapabilityDetailsAsync(int capabilityId, CancellationToken cancellationToken = default)
    {
        var capability = await dbContext.BusinessCapabilityCatalogItems
            .AsNoTracking()
            .Include(x => x.Mappings)
            .ThenInclude(x => x.BrmComponent)
            .ThenInclude(x => x!.ParentCapability)
            .ThenInclude(x => x!.ParentDomain)
            .Include(x => x.Mappings)
            .ThenInclude(x => x.ArmComponent)
            .ThenInclude(x => x!.ParentCapability)
            .ThenInclude(x => x!.ParentDomain)
            .AsSplitQuery()
            .FirstOrDefaultAsync(x => x.Id == capabilityId, cancellationToken);

        if (capability is null)
        {
            return null;
        }

        var armComponentIds = capability.Mappings
            .Select(x => x.ArmComponentId)
            .Distinct()
            .ToList();

        List<ApplicationCatalogItemMapping> applicationMappings = armComponentIds.Count == 0
            ? []
            : await dbContext.ApplicationCatalogItemMappings
                .AsNoTracking()
                .Where(x => armComponentIds.Contains(x.ArmComponentId))
                .Include(x => x.ApplicationCatalogItem)
                .Include(x => x.ArmComponent)
                .ThenInclude(x => x!.ParentCapability)
                .ThenInclude(x => x!.ParentDomain)
                .Include(x => x.ProductCatalogItem)
                .ThenInclude(x => x!.Mappings)
                .ThenInclude(x => x.TrmDomain)
                .Include(x => x.ProductCatalogItem)
                .ThenInclude(x => x!.Mappings)
                .ThenInclude(x => x.TrmCapability)
                .ThenInclude(x => x!.ParentDomain)
                .Include(x => x.ProductCatalogItem)
                .ThenInclude(x => x!.Mappings)
                .ThenInclude(x => x.TrmComponent)
                .ThenInclude(x => x!.ParentCapability)
                .ThenInclude(x => x!.ParentDomain)
                .AsSplitQuery()
                .ToListAsync(cancellationToken);

        var applicationCountByArmComponent = applicationMappings
            .GroupBy(x => x.ArmComponentId)
            .ToDictionary(
                group => group.Key,
                group => group.Select(x => x.ApplicationCatalogItemId).Distinct().Count());

        var mappingRows = capability.Mappings
            .OrderByDescending(x => x.IsPrimary)
            .ThenBy(x => x.BrmComponent!.Code)
            .ThenBy(x => x.ArmComponent!.Code)
            .Select(mapping => new CapabilityMappingRowViewModel
            {
                BrmDomainLabel = BuildBrmDomainLabel(mapping.BrmComponent),
                BrmCapabilityLabel = BuildBrmCapabilityLabel(mapping.BrmComponent),
                BrmComponentLabel = mapping.BrmComponent?.DisplayLabel ?? "-",
                ArmDomainLabel = BuildArmDomainLabel(mapping.ArmComponent),
                ArmCapabilityLabel = BuildArmCapabilityLabel(mapping.ArmComponent),
                ArmComponentLabel = mapping.ArmComponent?.DisplayLabel ?? "-",
                IsPrimary = mapping.IsPrimary,
                Notes = mapping.Notes,
                LinkedApplicationCount = applicationCountByArmComponent.GetValueOrDefault(mapping.ArmComponentId)
            })
            .ToList();

        var resolvedPaths = capability.Mappings
            .SelectMany(mapping => BuildCapabilityPaths(mapping, applicationMappings.Where(x => x.ArmComponentId == mapping.ArmComponentId)))
            .OrderBy(x => x.BrmDomainLabel)
            .ThenBy(x => x.BrmCapabilityLabel)
            .ThenBy(x => x.BrmComponentLabel)
            .ThenBy(x => x.ArmDomainLabel)
            .ThenBy(x => x.ArmCapabilityLabel)
            .ThenBy(x => x.ArmComponentLabel)
            .ThenBy(x => x.ApplicationName)
            .ThenBy(x => x.ProductLabel)
            .ThenBy(x => x.TrmDomainLabel)
            .ThenBy(x => x.TrmCapabilityLabel)
            .ThenBy(x => x.TrmComponentLabel)
            .ToList();

        return new CapabilityDetailsViewModel
        {
            Id = capability.Id,
            Name = capability.Name,
            Description = capability.Description,
            Notes = capability.Notes,
            UpdatedUtc = capability.UpdatedUtc,
            MappingRows = mappingRows,
            ResolvedPaths = resolvedPaths,
            BrmCapabilityCount = capability.Mappings.Select(x => x.BrmComponentId).Distinct().Count(),
            ArmComponentCount = capability.Mappings.Select(x => x.ArmComponentId).Distinct().Count(),
            ApplicationCount = resolvedPaths
                .Where(x => x.ApplicationName != "-")
                .Select(x => x.ApplicationName)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count(),
            ProductCount = resolvedPaths
                .Where(x => x.ProductLabel != "-")
                .Select(x => x.ProductLabel)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count()
        };
    }

    private static IEnumerable<ApplicationResolvedPathViewModel> BuildApplicationPaths(ApplicationCatalogItemMapping mapping)
    {
        var armDomainLabel = BuildArmDomainLabel(mapping.ArmComponent);
        var armCapabilityLabel = BuildArmCapabilityLabel(mapping.ArmComponent);
        var armComponentLabel = mapping.ArmComponent?.DisplayLabel ?? "-";
        var productLabel = BuildProductLabel(mapping.ProductCatalogItem);
        IEnumerable<ProductMapping> productMappings = mapping.ProductCatalogItem?.Mappings ?? Array.Empty<ProductMapping>();

        if (!productMappings.Any())
        {
            yield return new ApplicationResolvedPathViewModel
            {
                ArmDomainLabel = armDomainLabel,
                ArmCapabilityLabel = armCapabilityLabel,
                ArmComponentLabel = armComponentLabel,
                ProductLabel = productLabel
            };

            yield break;
        }

        foreach (var productMapping in productMappings)
        {
            yield return new ApplicationResolvedPathViewModel
            {
                ArmDomainLabel = armDomainLabel,
                ArmCapabilityLabel = armCapabilityLabel,
                ArmComponentLabel = armComponentLabel,
                ProductLabel = productLabel,
                TrmDomainLabel = BuildTrmDomainLabel(productMapping),
                TrmCapabilityLabel = BuildTrmCapabilityLabel(productMapping),
                TrmComponentLabel = productMapping.TrmComponent?.DisplayLabel ?? "-",
                MappingStatus = productMapping.MappingStatus.ToString()
            };
        }
    }

    private static IEnumerable<CapabilityResolvedPathViewModel> BuildCapabilityPaths(
        BusinessCapabilityCatalogItemMapping mapping,
        IEnumerable<ApplicationCatalogItemMapping> applicationMappings)
    {
        var brmDomainLabel = BuildBrmDomainLabel(mapping.BrmComponent);
        var brmCapabilityLabel = BuildBrmCapabilityLabel(mapping.BrmComponent);
        var brmComponentLabel = mapping.BrmComponent?.DisplayLabel ?? "-";
        var armDomainLabel = BuildArmDomainLabel(mapping.ArmComponent);
        var armCapabilityLabel = BuildArmCapabilityLabel(mapping.ArmComponent);
        var armComponentLabel = mapping.ArmComponent?.DisplayLabel ?? "-";

        foreach (var applicationMapping in applicationMappings)
        {
            IEnumerable<ProductMapping> productMappings = applicationMapping.ProductCatalogItem?.Mappings ?? Array.Empty<ProductMapping>();
            if (!productMappings.Any())
            {
                yield return new CapabilityResolvedPathViewModel
                {
                    BrmDomainLabel = brmDomainLabel,
                    BrmCapabilityLabel = brmCapabilityLabel,
                    BrmComponentLabel = brmComponentLabel,
                    ArmDomainLabel = armDomainLabel,
                    ArmCapabilityLabel = armCapabilityLabel,
                    ArmComponentLabel = armComponentLabel,
                    ApplicationName = applicationMapping.ApplicationCatalogItem?.Name ?? "-",
                    ProductLabel = BuildProductLabel(applicationMapping.ProductCatalogItem)
                };

                continue;
            }

            foreach (var productMapping in productMappings)
            {
                yield return new CapabilityResolvedPathViewModel
                {
                    BrmDomainLabel = brmDomainLabel,
                    BrmCapabilityLabel = brmCapabilityLabel,
                    BrmComponentLabel = brmComponentLabel,
                    ArmDomainLabel = armDomainLabel,
                    ArmCapabilityLabel = armCapabilityLabel,
                    ArmComponentLabel = armComponentLabel,
                    ApplicationName = applicationMapping.ApplicationCatalogItem?.Name ?? "-",
                    ProductLabel = BuildProductLabel(applicationMapping.ProductCatalogItem),
                    TrmDomainLabel = BuildTrmDomainLabel(productMapping),
                    TrmCapabilityLabel = BuildTrmCapabilityLabel(productMapping),
                    TrmComponentLabel = productMapping.TrmComponent?.DisplayLabel ?? "-",
                    MappingStatus = productMapping.MappingStatus.ToString()
                };
            }
        }
    }

    private static string BuildProductLabel(ProductCatalogItem? product) =>
        product is null
            ? "-"
            : string.IsNullOrWhiteSpace(product.Vendor)
                ? product.Name
                : $"{product.Name} ({product.Vendor})";

    private static string BuildArmDomainLabel(ArmComponent? component) =>
        component?.ParentCapability?.ParentDomain is null
            ? "-"
            : $"{component.ParentCapability.ParentDomain.Code} {component.ParentCapability.ParentDomain.Name}";

    private static string BuildArmCapabilityLabel(ArmComponent? component) =>
        component?.ParentCapability is null
            ? "-"
            : $"{component.ParentCapability.Code} {component.ParentCapability.Name}";

    private static string BuildBrmDomainLabel(BrmComponent? component) =>
        component?.ParentCapability?.ParentDomain is null
            ? "-"
            : $"{component.ParentCapability.ParentDomain.Code} {component.ParentCapability.ParentDomain.Name}";

    private static string BuildBrmCapabilityLabel(BrmComponent? component) =>
        component?.ParentCapability is null
            ? "-"
            : $"{component.ParentCapability.Code} {component.ParentCapability.Name}";

    private static string BuildTrmDomainLabel(ProductMapping mapping)
    {
        var capability = mapping.TrmComponent?.ParentCapability ?? mapping.TrmCapability;
        var domain = mapping.TrmComponent?.ParentCapability?.ParentDomain ?? capability?.ParentDomain ?? mapping.TrmDomain;
        return domain is null ? "-" : $"{domain.Code} {domain.Name}";
    }

    private static string BuildTrmCapabilityLabel(ProductMapping mapping)
    {
        var capability = mapping.TrmComponent?.ParentCapability ?? mapping.TrmCapability;
        return capability is null ? "-" : $"{capability.Code} {capability.Name}";
    }
}
