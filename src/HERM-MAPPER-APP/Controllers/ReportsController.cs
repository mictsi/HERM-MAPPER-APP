using HERMMapperApp.Data;
using HERMMapperApp.Models;
using HERMMapperApp.Services;
using HERMMapperApp.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Text;

namespace HERMMapperApp.Controllers;

[Authorize(Policy = AppPolicies.CatalogueRead)]
public sealed class ReportsController(
    AppDbContext dbContext,
    ModelDiagramReportService modelDiagramReportService,
    ReferenceModelDiagramService referenceModelDiagramService) : Controller
{
    private static readonly IReadOnlyList<TabularExportColumn> CompletedMappingExportColumns =
    [
        new("model", "MODEL"),
        new("domain", "DOMAIN"),
        new("capability", "CAPABILITY"),
        new("component", "COMPONENT"),
        new("product", "PRODUCT")
    ];

    private static readonly IReadOnlyList<TabularExportColumn> ApplicationExportColumns =
    [
        new("name", "Name"),
        new("description", "Description"),
        new("notes", "Notes"),
        new("armComponentCount", "ARM components"),
        new("productCount", "Products"),
        new("resolvedPathCount", "Resolved paths"),
        new("updatedUtc", "Updated UTC")
    ];

    private static readonly IReadOnlyList<TabularExportColumn> ServiceExportColumns =
    [
        new("name", "Name"),
        new("description", "Description"),
        new("owner", "Owner"),
        new("lifecycleStatus", "Lifecycle status"),
        new("assetCriticalityScore", "Asset criticality score"),
        new("products", "Products"),
        new("productCount", "Product count"),
        new("connectionCount", "Connection count"),
        new("updatedUtc", "Updated UTC")
    ];

    private static readonly IReadOnlyList<TabularExportColumn> BrmModelExportColumns =
    [
        new("name", "Name"),
        new("area", "Area"),
        new("description", "Description"),
        new("status", "Status"),
        new("capabilityCount", "Capability count"),
        new("updatedUtc", "Updated UTC")
    ];

    private static readonly IReadOnlyList<TabularExportColumn> DrmModelExportColumns =
    [
        new("name", "Name"),
        new("area", "Area"),
        new("description", "Description"),
        new("status", "Status"),
        new("dataEntityCount", "Data entity count"),
        new("updatedUtc", "Updated UTC")
    ];

    public Task<IActionResult> IndexAsync(string? lifecycleOwner = null, int? brmModelId = null, bool showBrmModelReport = false)
    {
        if (!ModelState.IsValid)
        {
            return Task.FromResult<IActionResult>(BadRequest(ModelState));
        }

        if (showBrmModelReport || brmModelId.HasValue)
        {
            return Task.FromResult<IActionResult>(RedirectToAction("BrmModelReport", new { brmModelId }));
        }

        if (!string.IsNullOrWhiteSpace(lifecycleOwner))
        {
            return Task.FromResult<IActionResult>(RedirectToAction("LifecycleStatusReport", new { lifecycleOwner }));
        }

        return Task.FromResult<IActionResult>(RedirectToAction("TrmModelReport"));
    }

    public async Task<IActionResult> TrmModelReportAsync()
        => View("TrmModelReport", await BuildReportsViewModelAsync());

    public async Task<IActionResult> ArmModelReportAsync()
        => View("ArmModelReport", await BuildReportsViewModelAsync());

    public async Task<IActionResult> TrmServiceDiagramReportAsync(int? serviceId = null)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        return View("TrmServiceDiagramReport", await BuildReportsViewModelAsync(serviceId: serviceId));
    }

    public async Task<IActionResult> ArmApplicationDiagramReportAsync(int? applicationId = null)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        return View("ArmApplicationDiagramReport", await BuildReportsViewModelAsync(applicationId: applicationId));
    }

    public async Task<IActionResult> BrmModelReportAsync(int? brmModelId = null)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        return View("BrmModelReport", await BuildReportsViewModelAsync(brmModelId: brmModelId));
    }

    public async Task<IActionResult> DrmModelReportAsync(int? drmModelId = null)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        return View("DrmModelReport", await BuildReportsViewModelAsync(drmModelId: drmModelId));
    }

    public async Task<IActionResult> MappingByOwnerReportAsync()
        => View("MappingByOwnerReport", await BuildReportsViewModelAsync());

    public async Task<IActionResult> ProductsByOwnerReportAsync()
        => View("ProductsByOwnerReport", await BuildReportsViewModelAsync());

    public async Task<IActionResult> OwnerTechnologyFlowReportAsync()
        => View("OwnerTechnologyFlowReport", await BuildReportsViewModelAsync());

    public async Task<IActionResult> SankeyReportAsync()
        => View("SankeyReport", await BuildReportsViewModelAsync());

    public async Task<IActionResult> IncomingConnectionsHeatmapReportAsync()
        => View("IncomingConnectionsHeatmapReport", await BuildReportsViewModelAsync());

    public async Task<IActionResult> IncomingConnectionsReportAsync()
        => View("IncomingConnectionsReport", await BuildReportsViewModelAsync());

    public async Task<IActionResult> LifecycleStatusReportAsync(string? lifecycleOwner = null)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        return View("LifecycleStatusReport", await BuildReportsViewModelAsync(lifecycleOwner: lifecycleOwner));
    }

    [Authorize(Policy = AppPolicies.AdminOnly)]
    public async Task<IActionResult> ExportDataAsync()
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        return View("ExportData", await BuildExportDataViewModelAsync());
    }

    private async Task<ReportsViewModel> BuildReportsViewModelAsync(
        string? lifecycleOwner = null,
        int? brmModelId = null,
        int? drmModelId = null,
        int? serviceId = null,
        int? applicationId = null,
        bool showBrmModelReport = false)
    {
        if (!ModelState.IsValid)
        {
            throw new InvalidOperationException("Reports view model cannot be built from an invalid model state.");
        }

        var mappings = await dbContext.ProductMappings
            .AsNoTracking()
            .Where(x =>
                x.MappingStatus == Models.MappingStatus.Complete &&
                x.TrmComponentId != null &&
                x.ProductCatalogItem != null &&
                !x.ProductCatalogItem.IsDeleted)
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
            .Where(x => !x.IsDeleted)
            .Include(x => x.Owners)
            .OrderBy(x => x.Name)
            .ToListAsync();

        var serviceConnections = await LoadServiceConnectionsAsync();

        var incomingConnections = BuildIncomingConnectionsReport(serviceConnections);

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
        var brmModels = await dbContext.BrmModels
            .AsNoTracking()
            .Where(x => !x.IsDeleted)
            .OrderBy(x => x.Name)
            .ThenBy(x => x.Area)
            .ToListAsync();
        var drmModels = await dbContext.DrmModels
            .AsNoTracking()
            .Where(x => !x.IsDeleted)
            .OrderBy(x => x.Name)
            .ThenBy(x => x.Area)
            .ToListAsync();
        var services = await dbContext.ServiceCatalogItems
            .AsNoTracking()
            .Where(x => !x.IsDeleted)
            .OrderBy(x => x.Name)
            .ThenBy(x => x.Owner)
            .ToListAsync();
        var applications = await dbContext.ApplicationCatalogItems
            .AsNoTracking()
            .Where(x => !x.IsDeleted)
            .OrderBy(x => x.Name)
            .ToListAsync();
        var selectedBrmModelId = brmModels.Any(x => x.Id == brmModelId)
            ? brmModelId
            : brmModels.FirstOrDefault()?.Id;
        var selectedDrmModelId = drmModels.Any(x => x.Id == drmModelId)
            ? drmModelId
            : drmModels.FirstOrDefault()?.Id;
        var selectedServiceId = services.Any(x => x.Id == serviceId)
            ? serviceId
            : services.FirstOrDefault()?.Id;
        var selectedApplicationId = applications.Any(x => x.Id == applicationId)
            ? applicationId
            : applications.FirstOrDefault()?.Id;

        var model = new ReportsViewModel
        {
            OwnerCount = paths.Select(x => x.OwnerName).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
            DomainCount = paths.Select(x => x.DomainId).Distinct().Count(),
            CapabilityCount = paths.Select(x => x.CapabilityId).Distinct().Count(),
            ComponentCount = paths.Select(x => x.ComponentId).Distinct().Count(),
            ProductCount = paths.Select(x => x.ProductId).Distinct().Count(),
            MappingPathCount = paths.Count,
            ExpandBrmModelReport = showBrmModelReport,
            SelectedBrmModelId = selectedBrmModelId,
            SelectedDrmModelId = selectedDrmModelId,
            SelectedServiceId = selectedServiceId,
            SelectedApplicationId = selectedApplicationId,
            SelectedLifecycleOwner = lifecycleOwner,
            LifecycleProductCount = lifecycleProducts.Count,
            ModelDiagram = await modelDiagramReportService.BuildAsync(),
            ArmModelDiagram = await referenceModelDiagramService.BuildArmAsync(),
            TrmServiceDiagram = await modelDiagramReportService.BuildForServiceAsync(selectedServiceId),
            ArmApplicationDiagram = await referenceModelDiagramService.BuildArmApplicationAsync(selectedApplicationId),
            BrmModelOptions = BuildBrmModelOptions(brmModels, selectedBrmModelId),
            DrmModelOptions = BuildDrmModelOptions(drmModels, selectedDrmModelId),
            ServiceOptions = BuildServiceOptions(services, selectedServiceId),
            ApplicationOptions = BuildApplicationOptions(applications, selectedApplicationId),
            BrmModelDiagram = await referenceModelDiagramService.BuildBrmModelAsync(selectedBrmModelId),
            DrmModelDiagram = await referenceModelDiagramService.BuildDrmModelAsync(selectedDrmModelId),
            AvailableOwners = availableOwners,
            LifecycleStatuses = BuildLifecycleStatuses(lifecycleProducts),
            Owners = BuildReportsHierarchy(paths),
            Paths = paths,
            IncomingConnections = incomingConnections,
            IncomingConnectionsHeatmap = BuildIncomingConnectionsHeatmap(incomingConnections),
            SankeyNodes = BuildReportsSankeyNodes(paths),
            SankeyLinks = BuildReportsSankeyLinks(paths)
        };

        return model;
    }

    public async Task<IActionResult> ModelDiagramAsync(string? scope = null, int? brmModelId = null, int? drmModelId = null, int? serviceId = null, int? applicationId = null)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        return View("ModelDiagram", await BuildModelDiagramAsync(scope, brmModelId, drmModelId, serviceId, applicationId));
    }

    public async Task<FileContentResult> DownloadModelDiagramSvgAsync(string? scope = null, int? brmModelId = null, int? drmModelId = null, int? serviceId = null, int? applicationId = null)
    {
        if (!ModelState.IsValid)
        {
            return File(Array.Empty<byte>(), "image/svg+xml", "invalid-request.svg");
        }

        var model = await BuildModelDiagramAsync(scope, brmModelId, drmModelId, serviceId, applicationId);
        var content = Encoding.UTF8.GetBytes(ModelDiagramPosterSvgService.BuildSvg(model));
        return File(content, "image/svg+xml", ModelDiagramPosterSvgService.BuildDownloadFileName(model.ScopeKey));
    }

    [Authorize(Policy = AppPolicies.AdminOnly)]
    public async Task<IActionResult> ExportMappingsCsvAsync()
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        return await DownloadExportAsync(ExportDataset.CompletedMappings, ExportFileFormat.Csv);
    }

    [Authorize(Policy = AppPolicies.AdminOnly)]
    public async Task<IActionResult> DownloadExportAsync(ExportDataset dataset, ExportFileFormat format)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        if (!Enum.IsDefined(dataset) || !Enum.IsDefined(format))
        {
            return BadRequest();
        }

        var table = await BuildExportTableAsync(dataset);
        var timestamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
        var fileStem = BuildExportFileStem(dataset);

        return format switch
        {
            ExportFileFormat.Csv => File(
                Encoding.UTF8.GetBytes(TabularExportService.BuildCsv(table)),
                "text/csv",
                $"{fileStem}-{timestamp}.csv"),
            ExportFileFormat.Json => File(
                Encoding.UTF8.GetBytes(TabularExportService.BuildJson(table)),
                "application/json",
                $"{fileStem}-{timestamp}.json"),
            ExportFileFormat.Xlsx => File(
                TabularExportService.BuildXlsx(table),
                TabularExportService.GetSpreadsheetContentType(),
                $"{fileStem}-{timestamp}.xlsx"),
            _ => BadRequest()
        };
    }

    public async Task<FileContentResult> DownloadDrawIoAsync(string? scope = null, int? brmModelId = null, int? drmModelId = null, int? serviceId = null, int? applicationId = null)
    {
        if (!ModelState.IsValid)
        {
            return File(Array.Empty<byte>(), "application/xml", "invalid-request.xml");
        }

        var normalizedScope = NormalizeScope(scope);
        if (normalizedScope == "trm" && !serviceId.HasValue)
        {
            var productContent = await modelDiagramReportService.BuildDrawIoAsync();
            return File(productContent, "application/xml", "herm-product-model.drawio");
        }

        var model = await BuildModelDiagramAsync(scope, brmModelId, drmModelId, serviceId, applicationId);
        var content = ModelDiagramExportService.BuildDrawIo(model);
        return File(content, "application/xml", ModelDiagramExportService.BuildDrawIoFileName(model));
    }

    public async Task<FileContentResult> DownloadArchiXmlAsync(string? scope = null, int? brmModelId = null, int? drmModelId = null, int? serviceId = null, int? applicationId = null)
    {
        if (!ModelState.IsValid)
        {
            return File(Array.Empty<byte>(), "application/xml", "invalid-request.xml");
        }

        var normalizedScope = NormalizeScope(scope);
        if (normalizedScope == "trm" && !serviceId.HasValue)
        {
            var productContent = await modelDiagramReportService.BuildArchiXmlAsync();
            return File(productContent, "application/xml", "herm-product-model.archimate.xml");
        }

        var model = await BuildModelDiagramAsync(scope, brmModelId, drmModelId, serviceId, applicationId);
        var content = ModelDiagramExportService.BuildArchiXml(model);
        return File(content, "application/xml", ModelDiagramExportService.BuildArchiXmlFileName(model));
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

    private static List<IncomingConnectionsReportRowViewModel> BuildIncomingConnectionsReport(
        List<ServiceProductConnectionRecord> connections) =>
        connections
            .GroupBy(connection => connection.ToProduct.Id)
            .Select(group =>
            {
                var targetProduct = group.First().ToProduct;
                var serviceNames = group
                    .Select(connection => connection.ServiceName)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                var sourceProducts = group
                    .Select(connection => BuildConnectionProductLabel(connection.FromProduct))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                return new IncomingConnectionsReportRowViewModel
                {
                    ProductId = targetProduct.Id,
                    ProductName = targetProduct.Name,
                    Vendor = targetProduct.Vendor,
                    Version = targetProduct.Version,
                    IncomingConnectionCount = group.Count(),
                    ServiceCount = serviceNames.Count,
                    ServicePreview = BuildPreviewLabel(serviceNames),
                    SourceProductPreview = BuildPreviewLabel(sourceProducts)
                };
            })
            .OrderByDescending(row => row.IncomingConnectionCount)
            .ThenByDescending(row => row.ServiceCount)
            .ThenBy(row => row.ProductName, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static List<IncomingConnectionsHeatmapNodeViewModel> BuildIncomingConnectionsHeatmap(
        IReadOnlyList<IncomingConnectionsReportRowViewModel> rows) =>
        rows
            .Select(row => new IncomingConnectionsHeatmapNodeViewModel
            {
                ProductId = row.ProductId,
                ProductName = row.ProductName,
                DisplayLabel = BuildIncomingConnectionsHeatmapLabel(row.ProductName, row.Vendor, row.Version),
                Vendor = row.Vendor,
                Version = row.Version,
                IncomingConnectionCount = row.IncomingConnectionCount,
                ServiceCount = row.ServiceCount
            })
            .OrderByDescending(row => row.IncomingConnectionCount)
            .ThenByDescending(row => row.ServiceCount)
            .ThenBy(row => row.ProductName, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private IQueryable<ProductMapping> BuildMappingsCsvQuery() =>
        dbContext.ProductMappings
            .AsNoTracking()
            .Include(x => x.ProductCatalogItem)
            .Include(x => x.TrmDomain)
            .Include(x => x.TrmCapability)
            .ThenInclude(x => x!.ParentDomain)
            .Include(x => x.TrmComponent)
            .ThenInclude(x => x!.ParentCapability)
            .ThenInclude(x => x!.ParentDomain)
            .Where(x =>
                x.MappingStatus == MappingStatus.Complete &&
                x.TrmComponentId != null &&
                x.ProductCatalogItem != null &&
                !x.ProductCatalogItem.IsDeleted);

    private async Task<ExportDataViewModel> BuildExportDataViewModelAsync()
    {
        var completedMappingCount = await BuildMappingsCsvQuery().CountAsync();
        var applicationCount = await dbContext.ApplicationCatalogItems.AsNoTracking().CountAsync(x => !x.IsDeleted);
        var serviceCount = await dbContext.ServiceCatalogItems.AsNoTracking().CountAsync(x => !x.IsDeleted);
        var brmModelCount = await dbContext.BrmModels.AsNoTracking().CountAsync(x => !x.IsDeleted);
        var drmModelCount = await dbContext.DrmModels.AsNoTracking().CountAsync(x => !x.IsDeleted);

        return new ExportDataViewModel
        {
            Datasets =
            [
                new ExportDatasetCardViewModel
                {
                    Dataset = ExportDataset.CompletedMappings,
                    Title = "Completed mappings",
                    Description = "Exports the uploaded sample layout for completed TRM mappings only.",
                    RecordCount = completedMappingCount,
                    RecordLabel = "mapping rows",
                    IncludedFields = CompletedMappingExportColumns.Select(column => column.Header).ToList()
                },
                new ExportDatasetCardViewModel
                {
                    Dataset = ExportDataset.Applications,
                    Title = "Applications",
                    Description = "Exports the application catalogue with description, notes, and resolved ARM or product counts.",
                    RecordCount = applicationCount,
                    RecordLabel = "application records",
                    IncludedFields = ApplicationExportColumns.Select(column => column.Header).ToList()
                },
                new ExportDatasetCardViewModel
                {
                    Dataset = ExportDataset.Services,
                    Title = "Services",
                    Description = "Exports active services with ownership, lifecycle, connected products, and flow counts.",
                    RecordCount = serviceCount,
                    RecordLabel = "service records",
                    IncludedFields = ServiceExportColumns.Select(column => column.Header).ToList()
                },
                new ExportDatasetCardViewModel
                {
                    Dataset = ExportDataset.BrmModels,
                    Title = "BRM models",
                    Description = "Exports business reference models with area, status, and capability totals.",
                    RecordCount = brmModelCount,
                    RecordLabel = "BRM records",
                    IncludedFields = BrmModelExportColumns.Select(column => column.Header).ToList()
                },
                new ExportDatasetCardViewModel
                {
                    Dataset = ExportDataset.DrmModels,
                    Title = "DRM models",
                    Description = "Exports data reference models with area, status, and data-entity totals.",
                    RecordCount = drmModelCount,
                    RecordLabel = "DRM records",
                    IncludedFields = DrmModelExportColumns.Select(column => column.Header).ToList()
                }
            ]
        };
    }

    private Task<TabularExportTable> BuildExportTableAsync(ExportDataset dataset)
        => dataset switch
        {
            ExportDataset.CompletedMappings => BuildCompletedMappingExportTableAsync(),
            ExportDataset.Applications => BuildApplicationExportTableAsync(),
            ExportDataset.Services => BuildServiceExportTableAsync(),
            ExportDataset.BrmModels => BuildBrmModelExportTableAsync(),
            ExportDataset.DrmModels => BuildDrmModelExportTableAsync(),
            _ => throw new InvalidOperationException($"Unsupported export dataset '{dataset}'.")
        };

    private async Task<TabularExportTable> BuildCompletedMappingExportTableAsync()
    {
        var mappings = await BuildMappingsCsvQuery()
            .OrderBy(x => x.TrmDomain != null ? x.TrmDomain.Name : string.Empty)
            .ThenBy(x => x.TrmComponent != null ? x.TrmComponent.Name : string.Empty)
            .ThenBy(x => x.ProductCatalogItem != null ? x.ProductCatalogItem.Name : string.Empty)
            .ToListAsync();

        var rows = mappings
            .Select(mapping =>
            {
                var capability = mapping.TrmComponent?.ParentCapability ?? mapping.TrmCapability;
                var domain = mapping.TrmComponent?.ParentCapability?.ParentDomain ?? capability?.ParentDomain ?? mapping.TrmDomain;

                return BuildExportRow(
                    ("model", "HERM"),
                    ("domain", domain is null ? null : $"{domain.Code} {domain.Name}"),
                    ("capability", capability is null ? null : $"{capability.Code} {capability.Name}"),
                    ("component", mapping.TrmComponent?.DisplayLabel),
                    ("product", mapping.ProductCatalogItem?.Name));
            })
            .ToList();

        return new TabularExportTable("Mappings", CompletedMappingExportColumns, rows);
    }

    private async Task<TabularExportTable> BuildApplicationExportTableAsync()
    {
        var applications = await dbContext.ApplicationCatalogItems
            .AsNoTracking()
            .Where(x => !x.IsDeleted)
            .OrderBy(x => x.Name)
            .Select(application => new
            {
                application.Name,
                application.Description,
                application.Notes,
                ArmComponentCount = application.Mappings
                    .Select(mapping => mapping.ArmComponentId)
                    .Distinct()
                    .Count(),
                ProductCount = application.Mappings
                    .Select(mapping => mapping.ProductCatalogItemId)
                    .Distinct()
                    .Count(),
                ResolvedPathCount = application.Mappings.Count,
                application.UpdatedUtc
            })
            .ToListAsync();

        var rows = applications
            .Select(application => BuildExportRow(
                ("name", application.Name),
                ("description", application.Description),
                ("notes", application.Notes),
                ("armComponentCount", application.ArmComponentCount.ToString(CultureInfo.InvariantCulture)),
                ("productCount", application.ProductCount.ToString(CultureInfo.InvariantCulture)),
                ("resolvedPathCount", application.ResolvedPathCount.ToString(CultureInfo.InvariantCulture)),
                ("updatedUtc", FormatUtc(application.UpdatedUtc))))
            .ToList();

        return new TabularExportTable("Applications", ApplicationExportColumns, rows);
    }

    private async Task<TabularExportTable> BuildServiceExportTableAsync()
    {
        var services = await dbContext.ServiceCatalogItems
            .AsNoTracking()
            .Where(x => !x.IsDeleted)
            .Include(x => x.ProductLinks.OrderBy(link => link.SortOrder))
            .ThenInclude(x => x.ProductCatalogItem)
            .Include(x => x.ProductConnections.OrderBy(connection => connection.SortOrder))
            .ThenInclude(x => x.FromProductCatalogItem)
            .Include(x => x.ProductConnections.OrderBy(connection => connection.SortOrder))
            .ThenInclude(x => x.ToProductCatalogItem)
            .AsSplitQuery()
            .OrderBy(x => x.Name)
            .ToListAsync();

        var rows = services
            .Select(service =>
            {
                var productNames = GetOrderedServiceProductLabels(service);

                return BuildExportRow(
                    ("name", service.Name),
                    ("description", service.Description),
                    ("owner", service.Owner),
                    ("lifecycleStatus", service.LifecycleStatus),
                    ("assetCriticalityScore", service.AssetCriticalityScore.ToString(CultureInfo.InvariantCulture)),
                    ("products", productNames.Count == 0 ? null : string.Join(" | ", productNames)),
                    ("productCount", productNames.Count.ToString(CultureInfo.InvariantCulture)),
                    ("connectionCount", service.ConnectionCount.ToString(CultureInfo.InvariantCulture)),
                    ("updatedUtc", FormatUtc(service.UpdatedUtc)));
            })
            .ToList();

        return new TabularExportTable("Services", ServiceExportColumns, rows);
    }

    private async Task<TabularExportTable> BuildBrmModelExportTableAsync()
    {
        var models = await dbContext.BrmModels
            .AsNoTracking()
            .Where(x => !x.IsDeleted)
            .OrderBy(x => x.Name)
            .ThenBy(x => x.Area)
            .Select(model => new
            {
                model.Name,
                model.Area,
                model.Description,
                model.Status,
                CapabilityCount = model.Capabilities.Count,
                model.UpdatedUtc
            })
            .ToListAsync();

        var rows = models
            .Select(model => BuildExportRow(
                ("name", model.Name),
                ("area", model.Area),
                ("description", model.Description),
                ("status", model.Status),
                ("capabilityCount", model.CapabilityCount.ToString(CultureInfo.InvariantCulture)),
                ("updatedUtc", FormatUtc(model.UpdatedUtc))))
            .ToList();

        return new TabularExportTable("BRM Models", BrmModelExportColumns, rows);
    }

    private async Task<TabularExportTable> BuildDrmModelExportTableAsync()
    {
        var models = await dbContext.DrmModels
            .AsNoTracking()
            .Where(x => !x.IsDeleted)
            .OrderBy(x => x.Name)
            .ThenBy(x => x.Area)
            .Select(model => new
            {
                model.Name,
                model.Area,
                model.Description,
                model.Status,
                DataEntityCount = model.DataEntities.Count,
                model.UpdatedUtc
            })
            .ToListAsync();

        var rows = models
            .Select(model => BuildExportRow(
                ("name", model.Name),
                ("area", model.Area),
                ("description", model.Description),
                ("status", model.Status),
                ("dataEntityCount", model.DataEntityCount.ToString(CultureInfo.InvariantCulture)),
                ("updatedUtc", FormatUtc(model.UpdatedUtc))))
            .ToList();

        return new TabularExportTable("DRM Models", DrmModelExportColumns, rows);
    }

    private static Dictionary<string, string?> BuildExportRow(params (string Key, string? Value)[] values)
    {
        var row = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var (key, value) in values)
        {
            row[key] = value;
        }

        return row;
    }

    private static string BuildExportFileStem(ExportDataset dataset)
        => dataset switch
        {
            ExportDataset.CompletedMappings => "herm-mappings-complete",
            ExportDataset.Applications => "herm-applications",
            ExportDataset.Services => "herm-services",
            ExportDataset.BrmModels => "herm-brm-models",
            ExportDataset.DrmModels => "herm-drm-models",
            _ => "herm-export"
        };

    private static string FormatUtc(DateTime value)
        => value.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture);

    private static List<string> GetOrderedServiceProductLabels(ServiceCatalogItem service)
    {
        if (service.ProductConnections.Count == 0)
        {
            return service.GetOrderedProductLinks()
                .Select(link => BuildProductLabel(link.ProductCatalogItem))
                .ToList();
        }

        var orderedConnections = service.GetOrderedProductConnections();
        var orderedProductIds = BuildOrderedGraphProductIds(
            orderedConnections
                .Select(connection => new ConnectionPair(connection.FromProductCatalogItemId, connection.ToProductCatalogItemId))
                .ToList(),
            out _);

        var labelsById = new Dictionary<int, string>();
        foreach (var connection in orderedConnections)
        {
            labelsById.TryAdd(connection.FromProductCatalogItemId, BuildProductLabel(connection.FromProductCatalogItem));
            labelsById.TryAdd(connection.ToProductCatalogItemId, BuildProductLabel(connection.ToProductCatalogItem));
        }

        return orderedProductIds
            .Where(labelsById.ContainsKey)
            .Select(id => labelsById[id])
            .ToList();
    }

    private static string BuildProductLabel(ProductCatalogItem product)
    {
        var detailParts = new[] { product.Vendor, product.Version }
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToList();

        return detailParts.Count == 0
            ? BuildDeletedProductLabel(product.Name, product.IsDeleted)
            : BuildDeletedProductLabel($"{product.Name} ({string.Join(" ", detailParts)})", product.IsDeleted);
    }

    private static string BuildDeletedProductLabel(string label, bool isDeleted) =>
        isDeleted ? $"{label} [deleted]" : label;

    private static List<int> BuildOrderedGraphProductIds(
        IReadOnlyList<ConnectionPair> connections,
        out bool supportsGraphLayout)
    {
        supportsGraphLayout = false;
        if (connections.Count == 0)
        {
            return [];
        }

        var firstAppearance = new Dictionary<int, int>();
        var adjacency = new Dictionary<int, HashSet<int>>();
        var indegree = new Dictionary<int, int>();
        var appearanceIndex = 0;

        static void EnsureNode(
            int productId,
            Dictionary<int, int> firstAppearance,
            Dictionary<int, HashSet<int>> adjacency,
            Dictionary<int, int> indegree,
            ref int appearanceIndex)
        {
            if (!firstAppearance.ContainsKey(productId))
            {
                firstAppearance[productId] = appearanceIndex++;
            }

            adjacency.TryAdd(productId, []);
            indegree.TryAdd(productId, 0);
        }

        foreach (var connection in connections)
        {
            EnsureNode(connection.FromProductId, firstAppearance, adjacency, indegree, ref appearanceIndex);
            EnsureNode(connection.ToProductId, firstAppearance, adjacency, indegree, ref appearanceIndex);

            if (adjacency[connection.FromProductId].Add(connection.ToProductId))
            {
                indegree[connection.ToProductId]++;
            }
        }

        var levels = indegree.Keys.ToDictionary(productId => productId, _ => 0);
        var remainingIndegree = indegree.ToDictionary(pair => pair.Key, pair => pair.Value);
        var ready = remainingIndegree
            .Where(pair => pair.Value == 0)
            .Select(pair => pair.Key)
            .OrderBy(productId => firstAppearance[productId])
            .ToList();

        var orderedIds = new List<int>();

        while (ready.Count != 0)
        {
            var currentProductId = ready[0];
            ready.RemoveAt(0);
            orderedIds.Add(currentProductId);

            foreach (var nextProductId in adjacency[currentProductId].OrderBy(productId => firstAppearance[productId]))
            {
                levels[nextProductId] = Math.Max(levels[nextProductId], levels[currentProductId] + 1);
                remainingIndegree[nextProductId]--;
                if (remainingIndegree[nextProductId] == 0)
                {
                    ready.Add(nextProductId);
                    ready.Sort((left, right) => firstAppearance[left].CompareTo(firstAppearance[right]));
                }
            }
        }

        if (orderedIds.Count != remainingIndegree.Count)
        {
            return firstAppearance
                .OrderBy(pair => pair.Value)
                .Select(pair => pair.Key)
                .ToList();
        }

        supportsGraphLayout = true;

        return orderedIds
            .OrderBy(productId => levels[productId])
            .ThenBy(productId => firstAppearance[productId])
            .ToList();
    }

    private sealed record ConnectionPair(int FromProductId, int ToProductId);

    private async Task<List<ServiceProductConnectionRecord>> LoadServiceConnectionsAsync()
    {
        var services = await dbContext.ServiceCatalogItems
            .AsNoTracking()
            .Where(x => !x.IsDeleted)
            .Include(x => x.ProductLinks.OrderBy(link => link.SortOrder))
            .ThenInclude(x => x.ProductCatalogItem)
            .Include(x => x.ProductConnections.OrderBy(connection => connection.SortOrder))
            .ThenInclude(x => x.FromProductCatalogItem)
            .Include(x => x.ProductConnections.OrderBy(connection => connection.SortOrder))
            .ThenInclude(x => x.ToProductCatalogItem)
            .AsSplitQuery()
            .ToListAsync();

        return services
            .SelectMany(BuildServiceConnections)
            .Where(connection => !connection.ToProduct.IsDeleted)
            .ToList();
    }

    private static IEnumerable<ServiceProductConnectionRecord> BuildServiceConnections(ServiceCatalogItem service)
    {
        if (service.ProductConnections.Count != 0)
        {
            return service.GetOrderedProductConnections()
                .Select(connection => new ServiceProductConnectionRecord(
                    service.Id,
                    service.Name,
                    connection.FromProductCatalogItem,
                    connection.ToProductCatalogItem))
                .ToList();
        }

        var orderedLinks = service.GetOrderedProductLinks();
        var connections = new List<ServiceProductConnectionRecord>();

        for (var index = 0; index < orderedLinks.Count - 1; index++)
        {
            connections.Add(new ServiceProductConnectionRecord(
                service.Id,
                service.Name,
                orderedLinks[index].ProductCatalogItem,
                orderedLinks[index + 1].ProductCatalogItem));
        }

        return connections;
    }

    private static string BuildConnectionProductLabel(ProductCatalogItem product)
    {
        var detailParts = new[] { product.Vendor, product.Version }
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToList();

        var label = detailParts.Count == 0
            ? product.Name
            : $"{product.Name} ({string.Join(" ", detailParts)})";

        return product.IsDeleted ? $"{label} [deleted]" : label;
    }

    private static string BuildPreviewLabel(List<string> values) => values.Count switch
    {
        0 => "-",
        <= 3 => string.Join(", ", values),
        _ => $"{string.Join(", ", values.Take(3))} +{values.Count - 3} more"
    };

    private static string BuildIncomingConnectionsHeatmapLabel(string productName, string? vendor, string? version)
    {
        var detail = string.Join(" ", new[] { vendor, version }.Where(value => !string.IsNullOrWhiteSpace(value)));
        return string.IsNullOrWhiteSpace(detail)
            ? productName
            : $"{productName}\n{detail}";
    }

    private static List<SelectListItem> BuildBrmModelOptions(
        IReadOnlyList<BrmModel> brmModels,
        int? selectedBrmModelId) =>
        brmModels
            .Select(x => new SelectListItem(
                string.IsNullOrWhiteSpace(x.Area)
                    ? $"{x.Name} ({x.Status})"
                    : $"{x.Name} - {x.Area} ({x.Status})",
                x.Id.ToString(CultureInfo.InvariantCulture),
                x.Id == selectedBrmModelId))
            .ToList();

    private static List<SelectListItem> BuildDrmModelOptions(
        IReadOnlyList<DrmModel> drmModels,
        int? selectedDrmModelId) =>
        drmModels
            .Select(x => new SelectListItem(
                string.IsNullOrWhiteSpace(x.Area)
                    ? $"{x.Name} ({x.Status})"
                    : $"{x.Name} - {x.Area} ({x.Status})",
                x.Id.ToString(CultureInfo.InvariantCulture),
                x.Id == selectedDrmModelId))
            .ToList();

    private static List<SelectListItem> BuildServiceOptions(
        IReadOnlyList<ServiceCatalogItem> services,
        int? selectedServiceId) =>
        services
            .Select(x => new SelectListItem(
                string.IsNullOrWhiteSpace(x.Owner)
                    ? x.Name
                    : $"{x.Name} - {x.Owner}",
                x.Id.ToString(CultureInfo.InvariantCulture),
                x.Id == selectedServiceId))
            .ToList();

    private static List<SelectListItem> BuildApplicationOptions(
        IReadOnlyList<ApplicationCatalogItem> applications,
        int? selectedApplicationId) =>
        applications
            .Select(x => new SelectListItem(
                x.Name,
                x.Id.ToString(CultureInfo.InvariantCulture),
                x.Id == selectedApplicationId))
            .ToList();

    private async Task<ModelDiagramReportViewModel> BuildModelDiagramAsync(string? scope, int? brmModelId, int? drmModelId, int? serviceId, int? applicationId)
    {
        var normalizedScope = NormalizeScope(scope);

        return normalizedScope switch
        {
            "arm" when applicationId.HasValue => await referenceModelDiagramService.BuildArmApplicationAsync(applicationId),
            "arm" => await referenceModelDiagramService.BuildArmAsync(),
            "brm" => await referenceModelDiagramService.BuildBrmModelAsync(brmModelId),
            "drm" => await referenceModelDiagramService.BuildDrmModelAsync(drmModelId),
            "trm" when serviceId.HasValue => await modelDiagramReportService.BuildForServiceAsync(serviceId),
            _ => await modelDiagramReportService.BuildAsync()
        };
    }

    private static string NormalizeScope(string? scope) =>
        string.IsNullOrWhiteSpace(scope)
            ? "trm"
            : scope.Trim().ToLowerInvariant();

    private sealed record ServiceProductConnectionRecord(
        int ServiceId,
        string ServiceName,
        ProductCatalogItem FromProduct,
        ProductCatalogItem ToProduct);
}
