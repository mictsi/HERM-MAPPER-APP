using HERMMapperApp.Data;
using HERMMapperApp.Models;
using HERMMapperApp.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HERMMapperApp.Controllers;

[Authorize(Policy = AppPolicies.CatalogueRead)]
public sealed class ReportsController(AppDbContext dbContext) : Controller
{
    public async Task<IActionResult> IndexAsync(string? lifecycleOwner = null)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        var mappings = await dbContext.ProductMappings
            .AsNoTracking()
            .Where(x => x.MappingStatus == Models.MappingStatus.Complete && x.TrmComponentId != null)
            .Include(x => x.ProductCatalogItem)
            .ThenInclude(x => x!.Owners)
            .Include(x => x.TrmDomain)
            .Include(x => x.TrmCapability)
            .ThenInclude(x => x!.ParentDomain)
            .Include(x => x.TrmComponent)
            .ThenInclude(x => x!.ParentCapability)
            .ThenInclude(x => x!.ParentDomain)
            .AsSplitQuery()
            .ToListAsync();

        var paths = mappings
            .SelectMany(BuildPathsForMapping)
            .OrderBy(x => x.OwnerName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.DomainLabel, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.CapabilityLabel, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.ComponentLabel, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.ProductName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var products = await dbContext.ProductCatalogItems
            .AsNoTracking()
            .Include(x => x.Owners)
            .OrderBy(x => x.Name)
            .ToListAsync();

        var availableOwners = products
            .SelectMany(x => x.GetOwnerValues())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (products.Exists(x => x.GetOwnerValues().Count == 0))
        {
            availableOwners.Insert(0, "Unassigned owner");
        }

        lifecycleOwner = string.IsNullOrWhiteSpace(lifecycleOwner)
            ? null
            : lifecycleOwner.Trim();

        var lifecycleProducts = FilterProductsByOwner(products, lifecycleOwner).ToList();

        var model = new ReportsViewModel
        {
            OwnerCount = paths.Select(x => x.OwnerName).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
            DomainCount = paths.Select(x => x.DomainId).Distinct().Count(),
            CapabilityCount = paths.Select(x => x.CapabilityId).Distinct().Count(),
            ComponentCount = paths.Select(x => x.ComponentId).Distinct().Count(),
            ProductCount = paths.Select(x => x.ProductId).Distinct().Count(),
            MappingPathCount = paths.Count,
            SelectedLifecycleOwner = lifecycleOwner,
            LifecycleProductCount = lifecycleProducts.Count,
            AvailableOwners = availableOwners,
            LifecycleStatuses = BuildLifecycleStatuses(lifecycleProducts),
            Owners = BuildReportsHierarchy(paths),
            Paths = paths,
            SankeyNodes = BuildReportsSankeyNodes(paths),
            SankeyLinks = BuildReportsSankeyLinks(paths)
        };

        return View(model);
    }

    private static IEnumerable<Models.ProductCatalogItem> FilterProductsByOwner(
        IEnumerable<Models.ProductCatalogItem> products,
        string? lifecycleOwner)
    {
        if (string.IsNullOrWhiteSpace(lifecycleOwner))
        {
            return products;
        }

        if (string.Equals(lifecycleOwner, "Unassigned owner", StringComparison.OrdinalIgnoreCase))
        {
            return products.Where(x => x.GetOwnerValues().Count == 0);
        }

        return products.Where(product =>
            product.GetOwnerValues().Exists(owner => string.Equals(owner, lifecycleOwner, StringComparison.OrdinalIgnoreCase)));
    }

    private static List<LifecycleStatusReportRowViewModel> BuildLifecycleStatuses(
        List<Models.ProductCatalogItem> products)
    {
        if (products.Count == 0)
        {
            return [];
        }

        return products
            .GroupBy(x => string.IsNullOrWhiteSpace(x.LifecycleStatus) ? "Not set" : x.LifecycleStatus!.Trim(), StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(x => x.Count())
            .ThenBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => new LifecycleStatusReportRowViewModel
            {
                Label = group.Key,
                ProductCount = group.Count(),
                Percentage = Math.Round((decimal)group.Count() / products.Count * 100m, 1, MidpointRounding.AwayFromZero),
                Products = group
                    .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(x => new LifecycleStatusProductViewModel
                    {
                        ProductId = x.Id,
                        Name = x.Name,
                        Vendor = x.Vendor,
                        Version = x.Version,
                        OwnersLabel = x.OwnerDisplay
                    })
                    .ToList()
            })
            .ToList();
    }

    private static IEnumerable<ReportsPathViewModel> BuildPathsForMapping(Models.ProductMapping mapping)
    {
        var product = mapping.ProductCatalogItem;
        var component = mapping.TrmComponent;
        var capability = component?.ParentCapability ?? mapping.TrmCapability;
        var domain = component?.ParentCapability?.ParentDomain ?? capability?.ParentDomain ?? mapping.TrmDomain;

        if (product is null || component is null || capability is null || domain is null)
        {
            return [];
        }

        var ownerValues = product.GetOwnerValues();
        IEnumerable<string> owners = ownerValues.Count == 0
            ? ["Unassigned owner"]
            : ownerValues;

        return owners.Select(owner => new ReportsPathViewModel
        {
            MappingId = mapping.Id,
            OwnerName = owner,
            DomainId = domain.Id,
            DomainLabel = $"{domain.Code} {domain.Name}",
            CapabilityId = capability.Id,
            CapabilityLabel = $"{capability.Code} {capability.Name}",
            ComponentId = component.Id,
            ComponentLabel = component.DisplayLabel,
            ProductId = product.Id,
            ProductName = product.Name
        });
    }

    private static List<ReportsHierarchyNodeViewModel> BuildReportsHierarchy(List<ReportsPathViewModel> paths)
    {
        var ownerGroups = paths
            .GroupBy(x => x.OwnerName, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(x => x.Count())
            .ThenBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return ownerGroups
            .Select(group => new ReportsHierarchyNodeViewModel
            {
                Key = $"owner:{group.Key}",
                NodeType = "owner",
                Label = group.Key,
                MappingCount = group.Count(),
                ProductCount = group.Select(x => x.ProductId).Distinct().Count(),
                IsExpanded = false,
                Children = BuildDomainNodes(group.ToList())
            })
            .ToList();
    }

    private static List<ReportsHierarchyNodeViewModel> BuildDomainNodes(List<ReportsPathViewModel> paths) =>
        paths.GroupBy(x => new { x.DomainId, x.DomainLabel })
            .OrderByDescending(x => x.Count())
            .ThenBy(x => x.Key.DomainLabel, StringComparer.OrdinalIgnoreCase)
            .Select(group => new ReportsHierarchyNodeViewModel
            {
                Key = $"domain:{group.Key.DomainId}",
                NodeType = "domain",
                Label = group.Key.DomainLabel,
                MappingCount = group.Count(),
                ProductCount = group.Select(x => x.ProductId).Distinct().Count(),
                Children = BuildCapabilityNodes(group.ToList())
            })
            .ToList();

    private static List<ReportsHierarchyNodeViewModel> BuildCapabilityNodes(List<ReportsPathViewModel> paths) =>
        paths.GroupBy(x => new { x.CapabilityId, x.CapabilityLabel })
            .OrderByDescending(x => x.Count())
            .ThenBy(x => x.Key.CapabilityLabel, StringComparer.OrdinalIgnoreCase)
            .Select(group => new ReportsHierarchyNodeViewModel
            {
                Key = $"capability:{group.Key.CapabilityId}",
                NodeType = "capability",
                Label = group.Key.CapabilityLabel,
                MappingCount = group.Count(),
                ProductCount = group.Select(x => x.ProductId).Distinct().Count(),
                Children = BuildComponentNodes(group.ToList())
            })
            .ToList();

    private static List<ReportsHierarchyNodeViewModel> BuildComponentNodes(List<ReportsPathViewModel> paths) =>
        paths.GroupBy(x => new { x.ComponentId, x.ComponentLabel })
            .OrderByDescending(x => x.Count())
            .ThenBy(x => x.Key.ComponentLabel, StringComparer.OrdinalIgnoreCase)
            .Select(group => new ReportsHierarchyNodeViewModel
            {
                Key = $"component:{group.Key.ComponentId}",
                NodeType = "component",
                Label = group.Key.ComponentLabel,
                MappingCount = group.Count(),
                ProductCount = group.Select(x => x.ProductId).Distinct().Count(),
                Children = BuildProductNodes(group.ToList())
            })
            .ToList();

    private static List<ReportsHierarchyNodeViewModel> BuildProductNodes(List<ReportsPathViewModel> paths) =>
        paths.GroupBy(x => new { x.ProductId, x.ProductName })
            .OrderByDescending(x => x.Count())
            .ThenBy(x => x.Key.ProductName, StringComparer.OrdinalIgnoreCase)
            .Select(group => new ReportsHierarchyNodeViewModel
            {
                Key = $"product:{group.Key.ProductId}",
                NodeType = "product",
                Label = group.Key.ProductName,
                MappingCount = group.Count(),
                ProductCount = 1,
                ProductId = group.Key.ProductId
            })
            .ToList();

    private static List<ReportsSankeyNodeViewModel> BuildReportsSankeyNodes(List<ReportsPathViewModel> paths)
    {
        var nodes = new List<ReportsSankeyNodeViewModel>();

        nodes.AddRange(paths
            .GroupBy(x => x.OwnerName, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(x => x.Count())
            .ThenBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => new ReportsSankeyNodeViewModel
            {
                Id = BuildSankeyNodeId("owner", group.Key),
                NodeType = "owner",
                Label = group.Key,
                Depth = 0,
                Value = group.Count()
            }));

        nodes.AddRange(paths
            .GroupBy(x => new { x.DomainId, x.DomainLabel })
            .OrderByDescending(x => x.Count())
            .ThenBy(x => x.Key.DomainLabel, StringComparer.OrdinalIgnoreCase)
            .Select(group => new ReportsSankeyNodeViewModel
            {
                Id = BuildSankeyNodeId("domain", group.Key.DomainId),
                NodeType = "domain",
                Label = group.Key.DomainLabel,
                Depth = 1,
                Value = group.Count()
            }));

        nodes.AddRange(paths
            .GroupBy(x => new { x.CapabilityId, x.CapabilityLabel })
            .OrderByDescending(x => x.Count())
            .ThenBy(x => x.Key.CapabilityLabel, StringComparer.OrdinalIgnoreCase)
            .Select(group => new ReportsSankeyNodeViewModel
            {
                Id = BuildSankeyNodeId("capability", group.Key.CapabilityId),
                NodeType = "capability",
                Label = group.Key.CapabilityLabel,
                Depth = 2,
                Value = group.Count()
            }));

        nodes.AddRange(paths
            .GroupBy(x => new { x.ComponentId, x.ComponentLabel })
            .OrderByDescending(x => x.Count())
            .ThenBy(x => x.Key.ComponentLabel, StringComparer.OrdinalIgnoreCase)
            .Select(group => new ReportsSankeyNodeViewModel
            {
                Id = BuildSankeyNodeId("component", group.Key.ComponentId),
                NodeType = "component",
                Label = group.Key.ComponentLabel,
                Depth = 3,
                Value = group.Count()
            }));

        nodes.AddRange(paths
            .GroupBy(x => new { x.ProductId, x.ProductName })
            .OrderByDescending(x => x.Count())
            .ThenBy(x => x.Key.ProductName, StringComparer.OrdinalIgnoreCase)
            .Select(group => new ReportsSankeyNodeViewModel
            {
                Id = BuildSankeyNodeId("product", group.Key.ProductId),
                NodeType = "product",
                Label = group.Key.ProductName,
                Depth = 4,
                Value = group.Count()
            }));

        return nodes;
    }

    private static List<ReportsSankeyLinkViewModel> BuildReportsSankeyLinks(List<ReportsPathViewModel> paths)
    {
        var links = new List<ReportsSankeyLinkViewModel>();

        links.AddRange(paths
            .GroupBy(x => new { x.OwnerName, x.DomainId })
            .Select(group => new ReportsSankeyLinkViewModel
            {
                SourceId = BuildSankeyNodeId("owner", group.Key.OwnerName),
                TargetId = BuildSankeyNodeId("domain", group.Key.DomainId),
                Value = group.Count(),
                LinkType = "owner-domain"
            }));

        links.AddRange(paths
            .GroupBy(x => new { x.DomainId, x.CapabilityId })
            .Select(group => new ReportsSankeyLinkViewModel
            {
                SourceId = BuildSankeyNodeId("domain", group.Key.DomainId),
                TargetId = BuildSankeyNodeId("capability", group.Key.CapabilityId),
                Value = group.Count(),
                LinkType = "domain-capability"
            }));

        links.AddRange(paths
            .GroupBy(x => new { x.CapabilityId, x.ComponentId })
            .Select(group => new ReportsSankeyLinkViewModel
            {
                SourceId = BuildSankeyNodeId("capability", group.Key.CapabilityId),
                TargetId = BuildSankeyNodeId("component", group.Key.ComponentId),
                Value = group.Count(),
                LinkType = "capability-component"
            }));

        links.AddRange(paths
            .GroupBy(x => new { x.ComponentId, x.ProductId })
            .Select(group => new ReportsSankeyLinkViewModel
            {
                SourceId = BuildSankeyNodeId("component", group.Key.ComponentId),
                TargetId = BuildSankeyNodeId("product", group.Key.ProductId),
                Value = group.Count(),
                LinkType = "component-product"
            }));

        return links
            .OrderByDescending(x => x.Value)
            .ThenBy(x => x.SourceId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.TargetId, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string BuildSankeyNodeId(string prefix, object value) => $"{prefix}:{value}";
}
