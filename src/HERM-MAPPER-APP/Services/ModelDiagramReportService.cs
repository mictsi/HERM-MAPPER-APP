using System.Globalization;
using System.Net;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using HERMMapperApp.Data;
using HERMMapperApp.Infrastructure;
using HERMMapperApp.Models;
using HERMMapperApp.ViewModels;
using Microsoft.EntityFrameworkCore;

namespace HERMMapperApp.Services;

public sealed class ModelDiagramReportService(AppDbContext dbContext)
{
    private const double DomainPadding = 19.685;
    private const double DomainTopOffset = 59.06;
    private const double DomainGapX = 19.685;
    private const double DomainGapY = 19.685;
    private const double CapabilityWidth = 188.98;
    private const double CapabilityGap = 11.812;
    private const double CapabilityInsetX = 11.811;
    private const double CapabilityComponentTop = 43.307;
    private const double CapabilityComponentGap = 7.874;
    private const double ProductInsetX = 5.9055;
    private const double ProductGap = 5.9055;
    private const double CanvasPadding = 19.685;
    private const double CanvasRowWidth = 2360.0;
    private const double MinComponentHeight = 59.055;
    private const int MaxPageWidth = 850;
    private const int MaxPageHeight = 1100;

    private const string CanvasGroupStyle = "group;recursiveResize=0;";
    private const string CanvasBackgroundStyle =
        "html=1;overflow=block;blockSpacing=1;whiteSpace=wrap;fontSize=16;spacingLeft=26.3;" +
        "spacingTop=7.5;align=left;spacing=0;verticalAlign=top;strokeOpacity=100;fillOpacity=100;" +
        "fillColor=#b2b2b2;strokeWidth=2;recursiveResize=0;";
    private const string DomainTitleStyle =
        "html=1;overflow=block;blockSpacing=1;whiteSpace=wrap;fontSize=24;fontFamily=Open Sans;" +
        "fontColor=#c92d39;fontStyle=1;align=left;spacingTop=9.8425;spacing=0;verticalAlign=top;" +
        "strokeOpacity=100;fillOpacity=100;rounded=1;absoluteArcSize=1;arcSize=7.5;fillColor=#ffffff;" +
        "strokeWidth=1.5;spacingLeft=19.685;recursiveResize=0;";
    private const string CapabilityGroupStyle = "group;recursiveResize=0;";
    private const string CapabilityStyle =
        "html=1;overflow=block;blockSpacing=1;whiteSpace=wrap;fontSize=16;fontFamily=Open Sans;" +
        "fontColor=default;align=left;spacingTop=4.5;spacing=0;verticalAlign=top;strokeOpacity=100;" +
        "fillOpacity=100;rounded=1;absoluteArcSize=1;arcSize=7.5;fillColor=#e5e5e5;strokeWidth=1.5;" +
        "spacingLeft=7.874;container=0;recursiveResize=0;";
    private const string ComponentContainerStyle =
        "html=1;overflow=block;blockSpacing=1;whiteSpace=wrap;fontSize=16;fontFamily=Open Sans;" +
        "fontColor=default;spacingTop=1.9685;align=left;spacing=0;verticalAlign=top;strokeOpacity=100;" +
        "fillOpacity=100;rounded=1;absoluteArcSize=1;arcSize=16;fillColor=#ffffff;strokeColor=#d9d9d9;" +
        "strokeWidth=1.2;spacingLeft=5.9055;container=0;";
    private const string ComponentTitleStyle =
        "html=1;overflow=block;blockSpacing=1;whiteSpace=wrap;fontSize=16;fontFamily=Open Sans;" +
        "fontColor=default;spacingTop=1.9685;align=left;spacing=0;verticalAlign=top;strokeColor=none;" +
        "fillColor=none;strokeWidth=0;spacingLeft=5.9055;container=0;";
    private const string ProductStyle =
        "html=1;overflow=block;blockSpacing=1;whiteSpace=wrap;fontSize=13;fontFamily=Open Sans;" +
        "fontColor=default;align=left;spacingTop=2;spacing=0;verticalAlign=middle;strokeOpacity=100;" +
        "fillOpacity=100;rounded=1;absoluteArcSize=1;arcSize=14;fillColor=#f6f4ef;strokeColor=#d6d0c3;" +
        "strokeWidth=1;spacingLeft=5.9055;container=0;";

    public async Task<ModelDiagramReportViewModel> BuildAsync(CancellationToken cancellationToken = default)
    {
        var data = await BuildDataAsync(productIds: null, cancellationToken: cancellationToken);
        return MapToViewModel(
            data,
            new DiagramMetadata(
                ScopeKey: "trm",
                ServiceId: null,
                ApplicationId: null,
                ReportFragmentId: "report-product-model",
                DiagramTitle: "TRM diagram (all objects)",
                DiagramDescription: "Browse the HERM structure with mapped products inside each component, open a full-screen canvas, or export the same data to draw.io and Archi XML.",
                PosterTitle: "Product model poster",
                PosterDescription: "Full-screen poster view of the HERM model with products placed directly inside each component column.",
                MappedItemLabel: "mapped product(s)",
                EmptyStateTitle: "No model content available",
                EmptyStateBody: "Import the HERM reference model and product mappings to populate this report.",
                BackReportAction: "TrmModelReport",
                BackReportLabel: "Back to TRM report",
                ShowUnmappedItems: true,
                ShowComponentMappedSummary: false,
                UnmappedSectionTitle: "Unmapped Products",
                UnmappedSummaryLabel: "product(s) still need a component placement",
                DrawIoDownloadAction: "DownloadDrawIo",
                ArchiDownloadAction: "DownloadArchiXml"));
    }

    public async Task<ModelDiagramReportViewModel> BuildForServiceAsync(int? serviceId, CancellationToken cancellationToken = default)
    {
        var selectedService = serviceId is > 0
            ? await dbContext.ServiceCatalogItems
                .AsNoTracking()
                .Where(x => !x.IsDeleted)
                .Include(x => x.ProductLinks.OrderBy(link => link.SortOrder))
                .Include(x => x.ProductConnections.OrderBy(connection => connection.SortOrder))
                .AsSplitQuery()
                .FirstOrDefaultAsync(x => x.Id == serviceId.Value, cancellationToken)
            : null;

        if (selectedService is null)
        {
            return new ModelDiagramReportViewModel
            {
                ScopeKey = "trm",
                ReportFragmentId = "report-trm-service",
                DiagramTitle = "TRM diagram per service",
                DiagramDescription = "Choose a service to review the TRM components touched by its connected products.",
                PosterTitle = "TRM service diagram poster",
                PosterDescription = "Full-screen poster view of the selected service across the TRM reference model.",
                MappedItemLabel = "mapped product(s)",
                EmptyStateTitle = "No service selected",
                EmptyStateBody = "Choose a service to build a TRM diagram for that service's mapped products.",
                BackReportAction = "TrmServiceDiagramReport",
                BackReportLabel = "Back to service TRM report",
                ShowUnmappedItems = true,
                ShowComponentMappedSummary = false,
                UnmappedSectionTitle = "Service Products Without TRM Placement",
                UnmappedSummaryLabel = "service product(s) still need a component placement",
                DrawIoDownloadAction = null,
                ArchiDownloadAction = null
            };
        }

        var productIds = GetServiceProductIds(selectedService);
        if (productIds.Count == 0)
        {
            return new ModelDiagramReportViewModel
            {
                ScopeKey = "trm",
                ServiceId = selectedService.Id,
                ReportFragmentId = "report-trm-service",
                DiagramTitle = "TRM diagram per service",
                DiagramDescription = BuildServiceDiagramDescription(selectedService),
                PosterTitle = $"{selectedService.Name} TRM service poster",
                PosterDescription = $"Full-screen poster view of {selectedService.Name} across the TRM reference model.",
                MappedItemLabel = "mapped product(s)",
                EmptyStateTitle = $"No connected products are mapped for {selectedService.Name}",
                EmptyStateBody = "Add products or product flows to this service to populate its TRM report.",
                BackReportAction = "TrmServiceDiagramReport",
                BackReportLabel = "Back to service TRM report",
                ShowUnmappedItems = true,
                ShowComponentMappedSummary = false,
                UnmappedSectionTitle = "Service Products Without TRM Placement",
                UnmappedSummaryLabel = "service product(s) still need a component placement",
                DrawIoDownloadAction = null,
                ArchiDownloadAction = null
            };
        }

        var data = await BuildDataAsync(productIds, cancellationToken);
        return MapToViewModel(
            data,
            new DiagramMetadata(
                ScopeKey: "trm",
                ServiceId: selectedService.Id,
                ApplicationId: null,
                ReportFragmentId: "report-trm-service",
                DiagramTitle: "TRM diagram per service",
                DiagramDescription: BuildServiceDiagramDescription(selectedService),
                PosterTitle: $"{selectedService.Name} TRM service poster",
                PosterDescription: $"Full-screen poster view of {selectedService.Name} across the TRM reference model with only that service's products rendered.",
                MappedItemLabel: "mapped product(s)",
                EmptyStateTitle: $"No TRM mappings found for {selectedService.Name}",
                EmptyStateBody: "Map the service's connected products to TRM components to populate this report.",
                BackReportAction: "TrmServiceDiagramReport",
                BackReportLabel: "Back to service TRM report",
                ShowUnmappedItems: true,
                ShowComponentMappedSummary: false,
                UnmappedSectionTitle: "Service Products Without TRM Placement",
                UnmappedSummaryLabel: "service product(s) still need a component placement",
                DrawIoDownloadAction: null,
                ArchiDownloadAction: null));
    }

    public async Task<byte[]> BuildDrawIoAsync(CancellationToken cancellationToken = default)
    {
        var data = await BuildDataAsync(productIds: null, cancellationToken: cancellationToken);
        var mxFile = new XElement("mxfile",
            new XAttribute("host", "app.diagrams.net"),
            new XAttribute("agent", "HERM Mapper"),
            new XAttribute("version", "29.6.3"),
            BuildDrawIoDiagram("Product Model Poster", BuildLayout(BuildExportDomains(data, includeProducts: true), includeProducts: true), includeProducts: true),
            BuildDrawIoDiagram("Model Catalogue", BuildLayout(BuildExportDomains(data, includeProducts: false), includeProducts: false), includeProducts: false));

        return SerializeXml(new XDocument(mxFile), includeDeclaration: false);
    }

    public async Task<byte[]> BuildArchiXmlAsync(CancellationToken cancellationToken = default)
    {
        var data = await BuildDataAsync(productIds: null, cancellationToken: cancellationToken);
        return SerializeXml(BuildArchiDocument(data), includeDeclaration: true);
    }

    private async Task<DiagramReportData> BuildDataAsync(IReadOnlyCollection<int>? productIds, CancellationToken cancellationToken)
    {
        var domains = await dbContext.TrmDomains
            .AsNoTracking()
            .ForReferenceModel(ReferenceModelKind.Trm)
            .OrderBy(x => x.Code)
            .ThenBy(x => x.Name)
            .ToListAsync(cancellationToken);

        var capabilities = await dbContext.TrmCapabilities
            .AsNoTracking()
            .ForReferenceModel(ReferenceModelKind.Trm)
            .OrderBy(x => x.ParentDomainId)
            .ThenBy(x => x.Code)
            .ThenBy(x => x.Name)
            .ToListAsync(cancellationToken);

        var components = await dbContext.TrmComponents
            .AsNoTracking()
            .ForReferenceModel(ReferenceModelKind.Trm)
            .Where(x => !x.IsDeleted)
            .OrderBy(x => x.ParentCapabilityId)
            .ThenBy(x => x.TechnologyComponentCode ?? x.Code)
            .ThenBy(x => x.Name)
            .ToListAsync(cancellationToken);

        var products = await dbContext.ProductCatalogItems
            .AsNoTracking()
            .Where(x => !x.IsDeleted)
            .Where(x => productIds == null || productIds.Contains(x.Id))
            .Include(x => x.Owners)
            .OrderBy(x => x.Name)
            .ToListAsync(cancellationToken);

        var mappings = await dbContext.ProductMappings
            .AsNoTracking()
            .Where(x => productIds == null || productIds.Contains(x.ProductCatalogItemId))
            .Include(x => x.TrmDomain)
            .Include(x => x.TrmCapability)
            .ThenInclude(x => x!.ParentDomain)
            .Include(x => x.TrmComponent)
            .ThenInclude(x => x!.ParentCapability)
            .ThenInclude(x => x!.ParentDomain)
            .AsSplitQuery()
            .OrderByDescending(x => x.UpdatedUtc)
            .ThenByDescending(x => x.Id)
            .ToListAsync(cancellationToken);

        var domainNodes = domains
            .Select(x => new DiagramDomainNode(x.Id, x.Code, x.Name))
            .ToList();
        var domainsById = domainNodes.ToDictionary(x => x.DomainId);
        var capabilitiesById = new Dictionary<int, DiagramCapabilityNode>();
        var componentsById = new Dictionary<int, DiagramComponentNode>();

        foreach (var capability in capabilities)
        {
            if (capability.ParentDomainId is not int domainId || !domainsById.TryGetValue(domainId, out var domainNode))
            {
                continue;
            }

            var capabilityNode = new DiagramCapabilityNode(capability.Id, capability.Code, capability.Name, domainNode);
            domainNode.Capabilities.Add(capabilityNode);
            capabilitiesById[capability.Id] = capabilityNode;
        }

        foreach (var component in components)
        {
            if (component.ParentCapabilityId is not int capabilityId || !capabilitiesById.TryGetValue(capabilityId, out var capabilityNode))
            {
                continue;
            }

            var componentNode = new DiagramComponentNode(component.Id, component.DisplayCode, component.Name, capabilityNode);
            capabilityNode.Components.Add(componentNode);
            componentsById[component.Id] = componentNode;
        }

        var productsById = products.ToDictionary(x => x.Id);
        var mappingsByProductId = mappings
            .Where(x => productsById.ContainsKey(x.ProductCatalogItemId))
            .GroupBy(x => x.ProductCatalogItemId)
            .ToDictionary(x => x.Key, x => x.ToList());

        var mappedProductIds = new HashSet<int>();
        var unmappedProducts = new List<DiagramProductNode>();

        foreach (var product in products)
        {
            var productMappings = mappingsByProductId.TryGetValue(product.Id, out var mappedItems)
                ? mappedItems
                : [];

            var resolvedPlacements = productMappings
                .Select(mapping => ResolvePlacement(mapping, componentsById))
                .Where(x => x is not null)
                .Cast<ResolvedPlacement>()
                .GroupBy(x => x.Component.ComponentId)
                .Select(group => group.First())
                .ToList();

            if (resolvedPlacements.Count == 0)
            {
                unmappedProducts.Add(BuildProductNode(product, ResolveFallbackStatus(productMappings)));
                continue;
            }

            mappedProductIds.Add(product.Id);

            foreach (var placement in resolvedPlacements)
            {
                placement.Component.Products.Add(BuildProductNode(product, GetStatusLabel(placement.Mapping.MappingStatus)));
            }
        }

        foreach (var component in domainNodes.SelectMany(x => x.Capabilities).SelectMany(x => x.Components))
        {
            component.Products.Sort((left, right) => StringComparer.OrdinalIgnoreCase.Compare(left.Name, right.Name));
        }

        unmappedProducts.Sort((left, right) => StringComparer.OrdinalIgnoreCase.Compare(left.Name, right.Name));

        return new DiagramReportData
        {
            Domains = domainNodes,
            ProductCount = products.Count,
            MappedProductCount = mappedProductIds.Count,
            UnmappedProducts = unmappedProducts
        };
    }

    private static ModelDiagramReportViewModel MapToViewModel(DiagramReportData data, DiagramMetadata metadata) =>
        new()
        {
            ScopeKey = metadata.ScopeKey,
            ServiceId = metadata.ServiceId,
            ApplicationId = metadata.ApplicationId,
            ReportFragmentId = metadata.ReportFragmentId,
            DiagramTitle = metadata.DiagramTitle,
            DiagramDescription = metadata.DiagramDescription,
            PosterTitle = metadata.PosterTitle,
            PosterDescription = metadata.PosterDescription,
            MappedItemLabel = metadata.MappedItemLabel,
            EmptyStateTitle = metadata.EmptyStateTitle,
            EmptyStateBody = metadata.EmptyStateBody,
            BackReportAction = metadata.BackReportAction,
            BackReportLabel = metadata.BackReportLabel,
            ShowUnmappedItems = metadata.ShowUnmappedItems,
            ShowComponentMappedSummary = metadata.ShowComponentMappedSummary,
            UnmappedSectionTitle = metadata.UnmappedSectionTitle,
            UnmappedSummaryLabel = metadata.UnmappedSummaryLabel,
            DrawIoDownloadAction = metadata.DrawIoDownloadAction,
            ArchiDownloadAction = metadata.ArchiDownloadAction,
            DomainCount = data.Domains.Count,
            CapabilityCount = data.Domains.Sum(x => x.Capabilities.Count),
            ComponentCount = data.Domains.Sum(x => x.Capabilities.Sum(capability => capability.Components.Count)),
            ProductCount = data.ProductCount,
            MappedProductCount = data.MappedProductCount,
            UnmappedProductCount = data.UnmappedProducts.Count,
            ItemCount = data.ProductCount,
            MappedItemCount = data.MappedProductCount,
            UnmappedItemCount = data.UnmappedProducts.Count,
            Domains = data.Domains.Select(MapDomain).ToList(),
            UnmappedProducts = data.UnmappedProducts.Select(MapProduct).ToList()
        };

    private static string BuildServiceDiagramDescription(ServiceCatalogItem service) =>
        $"Review {service.Name} across the full TRM reference model with only that service's connected products shown in their mapped components.";

    private static HashSet<int> GetServiceProductIds(ServiceCatalogItem service)
    {
        var productIds = new HashSet<int>();

        foreach (var link in service.ProductLinks)
        {
            productIds.Add(link.ProductCatalogItemId);
        }

        foreach (var connection in service.ProductConnections)
        {
            productIds.Add(connection.FromProductCatalogItemId);
            productIds.Add(connection.ToProductCatalogItemId);
        }

        return productIds;
    }

    private static ModelDiagramDomainViewModel MapDomain(DiagramDomainNode domain) =>
        new()
        {
            DomainId = domain.DomainId,
            Code = domain.Code,
            Name = domain.Name,
            Capabilities = domain.Capabilities.Select(MapCapability).ToList()
        };

    private static ModelDiagramCapabilityViewModel MapCapability(DiagramCapabilityNode capability) =>
        new()
        {
            CapabilityId = capability.CapabilityId,
            Code = capability.Code,
            Name = capability.Name,
            Components = capability.Components.Select(MapComponent).ToList()
        };

    private static ModelDiagramComponentViewModel MapComponent(DiagramComponentNode component) =>
        new()
        {
            ComponentId = component.ComponentId,
            Code = component.Code,
            Name = component.Name,
            Products = component.Products.Select(MapProduct).ToList()
        };

    private static ModelDiagramProductViewModel MapProduct(DiagramProductNode product) =>
        new()
        {
            ProductId = product.ProductId,
            Name = product.Name,
            StatusLabel = product.StatusLabel,
            StatusCssClass = GetStatusCssClass(product.StatusLabel),
            Vendor = product.Vendor,
            Version = product.Version,
            OwnersLabel = product.OwnersLabel,
            LinkController = "Products",
            LinkAction = "Details",
            LinkId = product.ProductId
        };

    private static ResolvedPlacement? ResolvePlacement(ProductMapping mapping, IReadOnlyDictionary<int, DiagramComponentNode> componentsById)
    {
        if (mapping.TrmComponentId is not int componentId || !componentsById.TryGetValue(componentId, out var componentNode))
        {
            return null;
        }

        return new ResolvedPlacement(componentNode.ParentCapability.ParentDomain, componentNode.ParentCapability, componentNode, mapping);
    }

    private static DiagramProductNode BuildProductNode(ProductCatalogItem product, string statusLabel) =>
        new(
            product.Id,
            product.Name,
            statusLabel,
            product.Vendor,
            product.Version,
            product.OwnerDisplay);

    private static string ResolveFallbackStatus(List<ProductMapping> mappings) =>
        mappings.Count == 0
            ? "Not mapped"
            : GetStatusLabel(mappings[0].MappingStatus);

    private static string GetStatusLabel(MappingStatus status) =>
        status switch
        {
            MappingStatus.Draft => "Draft",
            MappingStatus.InReview => "In Review",
            MappingStatus.Complete => "Complete",
            MappingStatus.OutOfScope => "Out of Scope",
            _ => "Unknown"
        };

    private static string GetStatusCssClass(string statusLabel) =>
        statusLabel switch
        {
            "Draft" => "draft",
            "In Review" => "in-review",
            "Complete" => "complete",
            "Out of Scope" => "out-of-scope",
            "Not mapped" => "not-mapped",
            _ => "unknown"
        };

    private static List<ExportDomainNode> BuildExportDomains(DiagramReportData data, bool includeProducts)
    {
        var domains = data.Domains
            .Select(domain => new ExportDomainNode(
                GetDomainElementId(domain.DomainId),
                domain.Code,
                domain.Name,
                domain.Capabilities
                    .Select(capability => new ExportCapabilityNode(
                        GetCapabilityElementId(capability.CapabilityId),
                        capability.Code,
                        capability.Name,
                        capability.Components
                            .Select(component => new ExportComponentNode(
                                GetComponentElementId(component.ComponentId),
                                component.Code,
                                component.Name,
                                includeProducts ? component.Products.ToList() : [],
                                IsProductProxy: false))
                            .ToList()))
                    .ToList()))
            .ToList();

        if (includeProducts && data.UnmappedProducts.Count > 0)
        {
            domains.Add(new ExportDomainNode(
                "element-domain-unmapped",
                "UNMAPPED",
                "Unmapped Products",
                [
                    new ExportCapabilityNode(
                        "element-capability-unmapped",
                        string.Empty,
                        "Pending Classification",
                        data.UnmappedProducts
                            .Select(product => new ExportComponentNode(
                                GetProductElementId(product.ProductId),
                                string.Empty,
                                product.Name,
                                [],
                                IsProductProxy: true))
                            .ToList())
                ]));
        }

        return domains;
    }

    private static DiagramLayout BuildLayout(IReadOnlyList<ExportDomainNode> domains, bool includeProducts)
    {
        var placements = new List<DiagramDomainPlacement>();
        var cursorX = CanvasPadding;
        var cursorY = CanvasPadding;
        var rowHeight = 0.0;
        var maxX = CanvasPadding;

        foreach (var domain in domains)
        {
            var capabilityPlacements = new List<DiagramCapabilityPlacement>();
            var capabilityHeights = new List<double>();

            foreach (var capability in domain.Capabilities)
            {
                var componentPlacements = new List<DiagramComponentPlacement>();
                var componentY = CapabilityComponentTop;

                foreach (var component in capability.Components)
                {
                    var placement = BuildComponentPlacement(component, includeProducts) with
                    {
                        X = CapabilityInsetX,
                        Y = componentY
                    };

                    componentPlacements.Add(placement);
                    componentY += placement.Height + CapabilityComponentGap;
                }

                var componentsHeight = componentPlacements.Count == 0
                    ? 0.0
                    : componentPlacements.Sum(x => x.Height) + (CapabilityComponentGap * (componentPlacements.Count - 1));

                var capabilityHeight = Math.Max(
                    CapabilityComponentTop + componentsHeight + CapabilityInsetX,
                    CapabilityComponentTop + MinComponentHeight + CapabilityInsetX);

                capabilityHeights.Add(capabilityHeight);
                capabilityPlacements.Add(new DiagramCapabilityPlacement(
                    capability,
                    0,
                    DomainTopOffset,
                    CapabilityWidth,
                    capabilityHeight,
                    componentPlacements));
            }

            var capabilityCount = Math.Max(1, domain.Capabilities.Count);
            var domainWidth = (DomainPadding * 2) + (capabilityCount * CapabilityWidth) + ((capabilityCount - 1) * CapabilityGap);
            var domainHeight = DomainTopOffset + (capabilityHeights.Count == 0 ? MinComponentHeight : capabilityHeights.Max()) + DomainPadding;

            if (cursorX > CanvasPadding && cursorX + domainWidth + CanvasPadding > CanvasRowWidth)
            {
                cursorX = CanvasPadding;
                cursorY += rowHeight + DomainGapY;
                rowHeight = 0.0;
            }

            for (var capabilityIndex = 0; capabilityIndex < capabilityPlacements.Count; capabilityIndex++)
            {
                var capabilityPlacement = capabilityPlacements[capabilityIndex];
                capabilityPlacements[capabilityIndex] = capabilityPlacement with
                {
                    X = DomainPadding + (capabilityIndex * (CapabilityWidth + CapabilityGap))
                };
            }

            placements.Add(new DiagramDomainPlacement(domain, cursorX, cursorY, domainWidth, domainHeight, capabilityPlacements));

            cursorX += domainWidth + DomainGapX;
            rowHeight = Math.Max(rowHeight, domainHeight);
            maxX = Math.Max(maxX, cursorX);
        }

        return new DiagramLayout(
            placements,
            maxX + CanvasPadding,
            cursorY + rowHeight + CanvasPadding);
    }

    private static DiagramComponentPlacement BuildComponentPlacement(ExportComponentNode component, bool includeProducts)
    {
        var headerHeight = EstimateComponentHeaderHeight(component.DisplayLabel);
        var productPlacements = new List<DiagramProductPlacement>();
        var productY = headerHeight + ProductGap;

        if (includeProducts && !component.IsProductProxy)
        {
            foreach (var product in component.Products)
            {
                var productHeight = EstimateProductHeight(product);
                productPlacements.Add(new DiagramProductPlacement(product, ProductInsetX, productY, 153.543, productHeight));
                productY += productHeight + ProductGap;
            }
        }

        var totalHeight = headerHeight;

        if (productPlacements.Count > 0)
        {
            totalHeight = productY;
        }

        totalHeight += ProductGap;
        totalHeight = Math.Max(MinComponentHeight, totalHeight);

        return new DiagramComponentPlacement(component, 0, 0, 165.354, totalHeight, headerHeight, productPlacements);
    }

    private static double EstimateComponentHeaderHeight(string value)
    {
        var lineCount = EstimateLineCount(value, 24);
        var estimatedHeight = 18 + (lineCount * 15) + 12;
        return Math.Max(44.0, estimatedHeight);
    }

    private static double EstimateProductHeight(DiagramProductNode product)
    {
        var lineCount = EstimateLineCount(BuildProductExportLabel(product), 22);
        return Math.Max(26.0, 10 + (lineCount * 12) + 6);
    }

    private static int EstimateLineCount(string value, int widthChars)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return 0;
        }

        var lineCount = 0;

        foreach (var paragraph in value.Split('\n'))
        {
            var segment = paragraph.Trim();

            if (segment.Length == 0)
            {
                lineCount++;
                continue;
            }

            var words = segment.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var currentWidth = 0;

            foreach (var wordLength in words.Select(word => word.Length))
            {
                if (currentWidth == 0)
                {
                    currentWidth = wordLength;
                    continue;
                }

                if (currentWidth + 1 + wordLength > widthChars)
                {
                    lineCount++;
                    currentWidth = wordLength;
                }
                else
                {
                    currentWidth += 1 + wordLength;
                }
            }

            lineCount++;
        }

        return Math.Max(1, lineCount);
    }

    private static XElement BuildDrawIoDiagram(string pageName, DiagramLayout layout, bool includeProducts)
    {
        var builder = new DrawIoBuilder();
        var canvasGroupId = builder.AddGroup("1", 20.0, 20.0, layout.CanvasWidth, layout.CanvasHeight, CanvasGroupStyle);
        builder.AddVertex(canvasGroupId, 0.0, 0.0, layout.CanvasWidth, layout.CanvasHeight, CanvasBackgroundStyle, string.Empty);

        foreach (var domain in layout.Domains)
        {
            var domainGroupId = builder.AddGroup(canvasGroupId, domain.X, domain.Y, domain.Width, domain.Height, CanvasGroupStyle);
            builder.AddVertex(
                domainGroupId,
                0.0,
                0.0,
                domain.Width,
                domain.Height,
                DomainTitleStyle,
                $"<span style=\"font-size: 28px; background-color: initial;\">{WebUtility.HtmlEncode(domain.Domain.Name.ToUpperInvariant())}</span>");

            foreach (var capability in domain.Capabilities)
            {
                var capabilityGroupId = builder.AddGroup(
                    domainGroupId,
                    capability.X,
                    capability.Y,
                    capability.Width,
                    capability.Height,
                    CapabilityGroupStyle);

                builder.AddVertex(
                    capabilityGroupId,
                    0.0,
                    0.0,
                    capability.Width,
                    capability.Height,
                    CapabilityStyle,
                    WebUtility.HtmlEncode(capability.Capability.DisplayLabel));

                foreach (var component in capability.Components)
                {
                    var componentGroupId = builder.AddGroup(
                        capabilityGroupId,
                        component.X,
                        component.Y,
                        component.Width,
                        component.Height,
                        CapabilityGroupStyle);

                    builder.AddVertex(
                        componentGroupId,
                        0,
                        0,
                        component.Width,
                        component.Height,
                        component.Component.IsProductProxy ? ProductStyle : ComponentContainerStyle,
                        string.Empty);

                    builder.AddVertex(
                        componentGroupId,
                        0,
                        0,
                        component.Width,
                        component.Component.IsProductProxy ? component.Height : component.HeaderHeight,
                        ComponentTitleStyle,
                        WebUtility.HtmlEncode(component.Component.DisplayLabel));

                    if (includeProducts && !component.Component.IsProductProxy)
                    {
                        foreach (var product in component.Products)
                        {
                            builder.AddVertex(
                                componentGroupId,
                                product.X,
                                product.Y,
                                product.Width,
                                product.Height,
                                ProductStyle,
                                WebUtility.HtmlEncode(BuildProductExportLabel(product.Product)));
                        }
                    }
                }
            }
        }

        return new XElement("diagram",
            new XAttribute("name", pageName),
            new XAttribute("id", pageName.ToLowerInvariant().Replace(' ', '-')),
            new XElement("mxGraphModel",
                new XAttribute("dx", ((int)(layout.CanvasWidth + 700)).ToString(CultureInfo.InvariantCulture)),
                new XAttribute("dy", ((int)(layout.CanvasHeight + 700)).ToString(CultureInfo.InvariantCulture)),
                new XAttribute("grid", "1"),
                new XAttribute("gridSize", "10"),
                new XAttribute("guides", "1"),
                new XAttribute("tooltips", "1"),
                new XAttribute("connect", "1"),
                new XAttribute("arrows", "1"),
                new XAttribute("fold", "1"),
                new XAttribute("page", "1"),
                new XAttribute("pageScale", "1"),
                new XAttribute("pageWidth", MaxPageWidth.ToString(CultureInfo.InvariantCulture)),
                new XAttribute("pageHeight", MaxPageHeight.ToString(CultureInfo.InvariantCulture)),
                new XAttribute("math", "0"),
                new XAttribute("shadow", "0"),
                builder.Root));
    }

    private static string BuildProductExportLabel(DiagramProductNode product) => product.Name;

    private static XDocument BuildArchiDocument(DiagramReportData data)
    {
        XNamespace archimate = "http://www.opengroup.org/xsd/archimate/3.0/";
        XNamespace xsi = "http://www.w3.org/2001/XMLSchema-instance";
        XNamespace xml = XNamespace.Xml;
        var posterLayout = BuildLayout(BuildExportDomains(data, includeProducts: true), includeProducts: true);

        var model = new XElement(archimate + "model",
            new XAttribute(XNamespace.Xmlns + "xsi", xsi),
            new XAttribute("identifier", "model-herm-product-diagram"),
            new XElement(archimate + "name", new XAttribute(xml + "lang", "en"), "HERM Product Model"),
            new XElement(archimate + "documentation", new XAttribute(xml + "lang", "en"), "Generated from HERM Mapper product and model data."));

        var elements = new XElement(archimate + "elements");
        var relationships = new XElement(archimate + "relationships");
        var organizations = new XElement(archimate + "organizations");

        foreach (var domain in data.Domains)
        {
            elements.Add(BuildArchiElement(archimate, xsi, xml, GetDomainElementId(domain.DomainId), "Grouping", domain.DisplayLabel, $"Domain code: {domain.Code}"));

            foreach (var capability in domain.Capabilities)
            {
                elements.Add(BuildArchiElement(archimate, xsi, xml, GetCapabilityElementId(capability.CapabilityId), "Capability", capability.DisplayLabel, $"Capability code: {capability.Code}"));
                relationships.Add(BuildArchiRelationship(
                    archimate,
                    xsi,
                    xml,
                    GetDomainCapabilityRelationshipId(domain.DomainId, capability.CapabilityId),
                    "Composition",
                    GetDomainElementId(domain.DomainId),
                    GetCapabilityElementId(capability.CapabilityId),
                    "Contains"));

                foreach (var component in capability.Components)
                {
                    elements.Add(BuildArchiElement(archimate, xsi, xml, GetComponentElementId(component.ComponentId), "TechnologyService", component.DisplayLabel, $"Component code: {component.Code}"));
                    relationships.Add(BuildArchiRelationship(
                        archimate,
                        xsi,
                        xml,
                        GetCapabilityComponentRelationshipId(capability.CapabilityId, component.ComponentId),
                        "Composition",
                        GetCapabilityElementId(capability.CapabilityId),
                        GetComponentElementId(component.ComponentId),
                        "Includes"));
                }
            }
        }

        var allProducts = data.Domains
            .SelectMany(x => x.Capabilities)
            .SelectMany(x => x.Components)
            .SelectMany(x => x.Products)
            .Concat(data.UnmappedProducts)
            .GroupBy(x => x.ProductId)
            .Select(group => group.First())
            .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var product in allProducts)
        {
            elements.Add(BuildArchiElement(
                archimate,
                xsi,
                xml,
                GetProductElementId(product.ProductId),
                "ApplicationComponent",
                product.Name,
                BuildProductDocumentation(product)));
        }

        foreach (var component in data.Domains.SelectMany(x => x.Capabilities).SelectMany(x => x.Components))
        {
            relationships.Add(component.Products.Select(product => BuildArchiRelationship(
                archimate,
                xsi,
                xml,
                GetComponentProductRelationshipId(component.ComponentId, product.ProductId),
                "Association",
                GetComponentElementId(component.ComponentId),
                GetProductElementId(product.ProductId),
                "Mapped product")));
        }

        if (data.UnmappedProducts.Count > 0)
        {
            elements.Add(BuildArchiElement(archimate, xsi, xml, "element-domain-unmapped", "Grouping", "Unmapped Products"));
            elements.Add(BuildArchiElement(archimate, xsi, xml, "element-capability-unmapped", "Grouping", "Pending Classification"));
            relationships.Add(BuildArchiRelationship(
                archimate,
                xsi,
                xml,
                "relationship-domain-unmapped-capability-unmapped",
                "Composition",
                "element-domain-unmapped",
                "element-capability-unmapped",
                "Contains"));

            relationships.Add(data.UnmappedProducts.Select(product => BuildArchiRelationship(
                archimate,
                xsi,
                xml,
                $"relationship-capability-unmapped-product-{product.ProductId}",
                "Association",
                "element-capability-unmapped",
                GetProductElementId(product.ProductId),
                "Pending product")));
        }

        organizations.Add(BuildOrganizationFolder(
            archimate,
            xml,
            "Domains",
            BuildDomainOrganizationItems(archimate, xml, data)));

        organizations.Add(BuildOrganizationFolder(
            archimate,
            xml,
            "Products",
            allProducts.Select(product => BuildOrganizationReference(
                archimate,
                xml,
                GetProductElementId(product.ProductId),
                product.Name))));

        var views = new XElement(archimate + "views",
            new XElement(archimate + "diagrams",
                new XElement(archimate + "view",
                    new XAttribute("identifier", "view-product-model-poster"),
                    new XAttribute(xsi + "type", "Diagram"),
                    new XElement(archimate + "name", new XAttribute(xml + "lang", "en"), "Product model poster"),
                    posterLayout.Domains.Select(domain => BuildArchiDiagramNode(archimate, xsi, domain)))));

        model.Add(elements);
        model.Add(relationships);
        model.Add(organizations);
        model.Add(views);

        return new XDocument(new XDeclaration("1.0", "utf-8", null), model);
    }

    private static IEnumerable<XElement> BuildDomainOrganizationItems(XNamespace archimate, XNamespace xml, DiagramReportData data)
    {
        foreach (var domain in data.Domains)
        {
            yield return BuildOrganizationReference(
                archimate,
                xml,
                GetDomainElementId(domain.DomainId),
                domain.DisplayLabel,
                domain.Capabilities.Select(capability =>
                    BuildOrganizationReference(
                        archimate,
                        xml,
                        GetCapabilityElementId(capability.CapabilityId),
                        capability.DisplayLabel,
                        capability.Components.Select(component =>
                            BuildOrganizationReference(
                                archimate,
                                xml,
                                GetComponentElementId(component.ComponentId),
                                component.DisplayLabel)))));
        }

        if (data.UnmappedProducts.Count > 0)
        {
            yield return BuildOrganizationReference(
                archimate,
                xml,
                "element-domain-unmapped",
                "Unmapped Products",
                [
                    BuildOrganizationReference(archimate, xml, "element-capability-unmapped", "Pending Classification")
                ]);
        }
    }

    private static XElement BuildArchiElement(
        XNamespace archimate,
        XNamespace xsi,
        XNamespace xml,
        string identifier,
        string type,
        string name,
        string? documentation = null)
    {
        var element = new XElement(archimate + "element",
            new XAttribute("identifier", identifier),
            new XAttribute(xsi + "type", type),
            new XElement(archimate + "name", new XAttribute(xml + "lang", "en"), name));

        if (!string.IsNullOrWhiteSpace(documentation))
        {
            element.Add(new XElement(archimate + "documentation", new XAttribute(xml + "lang", "en"), documentation));
        }

        return element;
    }

    private static XElement BuildArchiRelationship(
        XNamespace archimate,
        XNamespace xsi,
        XNamespace xml,
        string identifier,
        string type,
        string source,
        string target,
        string name,
        string? documentation = null)
    {
        var relationship = new XElement(archimate + "relationship",
            new XAttribute("identifier", identifier),
            new XAttribute(xsi + "type", type),
            new XAttribute("source", source),
            new XAttribute("target", target),
            new XElement(archimate + "name", new XAttribute(xml + "lang", "en"), name));

        if (!string.IsNullOrWhiteSpace(documentation))
        {
            relationship.Add(new XElement(archimate + "documentation", new XAttribute(xml + "lang", "en"), documentation));
        }

        return relationship;
    }

    private static XElement BuildArchiDiagramNode(XNamespace archimate, XNamespace xsi, DiagramDomainPlacement domain)
    {
        var node = BuildArchiViewNode(
            archimate,
            xsi,
            $"node-{domain.Domain.ElementId}",
            domain.Domain.ElementId,
            domain.X,
            domain.Y,
            domain.Width,
            domain.Height,
            255, 255, 255,
            201, 45, 57,
            14);

        foreach (var capability in domain.Capabilities)
        {
            var capabilityNode = BuildArchiViewNode(
                archimate,
                xsi,
                $"node-{capability.Capability.ElementId}",
                capability.Capability.ElementId,
                capability.X,
                capability.Y,
                capability.Width,
                capability.Height,
                229, 229, 229,
                120, 120, 120,
                11);

            foreach (var component in capability.Components)
            {
                var isProductProxy = component.Component.IsProductProxy;
                var componentNode = BuildArchiViewNode(
                    archimate,
                    xsi,
                    $"node-{capability.Capability.ElementId}-{component.Component.ElementId}",
                    component.Component.ElementId,
                    component.X,
                    component.Y,
                    component.Width,
                    component.Height,
                    isProductProxy ? 246 : 255,
                    isProductProxy ? 244 : 255,
                    isProductProxy ? 239 : 255,
                    isProductProxy ? 214 : 172,
                    isProductProxy ? 208 : 172,
                    isProductProxy ? 195 : 172,
                    10);

                foreach (var product in component.Products)
                {
                    componentNode.Add(BuildArchiViewNode(
                        archimate,
                        xsi,
                        $"node-{capability.Capability.ElementId}-{component.Component.ElementId}-product-{product.Product.ProductId}",
                        GetProductElementId(product.Product.ProductId),
                        product.X,
                        product.Y,
                        product.Width,
                        product.Height,
                        246, 244, 239,
                        214, 208, 195,
                        9));
                }

                capabilityNode.Add(componentNode);
            }

            node.Add(capabilityNode);
        }

        return node;
    }

    private static XElement BuildArchiViewNode(
        XNamespace archimate,
        XNamespace xsi,
        string identifier,
        string elementRef,
        double x,
        double y,
        double width,
        double height,
        int fillR,
        int fillG,
        int fillB,
        int lineR,
        int lineG,
        int lineB,
        int fontSize)
    {
        return new XElement(archimate + "node",
            new XAttribute("identifier", identifier),
            new XAttribute("elementRef", elementRef),
            new XAttribute("x", ((int)Math.Round(x)).ToString(CultureInfo.InvariantCulture)),
            new XAttribute("y", ((int)Math.Round(y)).ToString(CultureInfo.InvariantCulture)),
            new XAttribute("w", ((int)Math.Round(width)).ToString(CultureInfo.InvariantCulture)),
            new XAttribute("h", ((int)Math.Round(height)).ToString(CultureInfo.InvariantCulture)),
            new XAttribute(xsi + "type", "Element"),
            new XElement(archimate + "style",
                new XElement(archimate + "fillColor",
                    new XAttribute("r", fillR),
                    new XAttribute("g", fillG),
                    new XAttribute("b", fillB)),
                new XElement(archimate + "lineColor",
                    new XAttribute("r", lineR),
                    new XAttribute("g", lineG),
                    new XAttribute("b", lineB)),
                new XElement(archimate + "font",
                    new XAttribute("name", "Open Sans"),
                    new XAttribute("size", fontSize),
                    new XElement(archimate + "color",
                        new XAttribute("r", 31),
                        new XAttribute("g", 41),
                        new XAttribute("b", 51)))));
    }

    private static XElement BuildOrganizationFolder(
        XNamespace archimate,
        XNamespace xml,
        string label,
        IEnumerable<XElement> children)
    {
        var item = new XElement(archimate + "item",
            new XElement(archimate + "label", new XAttribute(xml + "lang", "en"), label));

        item.Add(children);
        return item;
    }

    private static XElement BuildOrganizationReference(
        XNamespace archimate,
        XNamespace xml,
        string identifierRef,
        string label,
        IEnumerable<XElement>? children = null)
    {
        var item = new XElement(archimate + "item",
            new XAttribute("identifierRef", identifierRef),
            new XElement(archimate + "label", new XAttribute(xml + "lang", "en"), label));

        if (children is not null)
        {
            item.Add(children);
        }

        return item;
    }

    private static string BuildProductDocumentation(DiagramProductNode product)
    {
        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(product.Vendor) || !string.IsNullOrWhiteSpace(product.Version))
        {
            parts.Add($"Version: {$"{product.Vendor} {product.Version}".Trim()}");
        }

        return string.Join("; ", parts);
    }

    private static byte[] SerializeXml(XDocument document, bool includeDeclaration)
    {
        using var stream = new MemoryStream();
        using (var writer = XmlWriter.Create(stream, new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(false),
            Indent = true,
            OmitXmlDeclaration = !includeDeclaration
        }))
        {
            document.Save(writer);
        }

        return stream.ToArray();
    }

    private static string GetDomainElementId(int domainId) => $"element-domain-{domainId}";

    private static string GetCapabilityElementId(int capabilityId) => $"element-capability-{capabilityId}";

    private static string GetComponentElementId(int componentId) => $"element-component-{componentId}";

    private static string GetProductElementId(int productId) => $"element-product-{productId}";

    private static string GetDomainCapabilityRelationshipId(int domainId, int capabilityId) => $"relationship-domain-{domainId}-capability-{capabilityId}";

    private static string GetCapabilityComponentRelationshipId(int capabilityId, int componentId) => $"relationship-capability-{capabilityId}-component-{componentId}";

    private static string GetComponentProductRelationshipId(int componentId, int productId) => $"relationship-component-{componentId}-product-{productId}";

    private sealed class DrawIoBuilder
    {
        private readonly XElement root =
            new("root",
                new XElement("mxCell", new XAttribute("id", "0")),
                new XElement("mxCell", new XAttribute("id", "1"), new XAttribute("parent", "0")));

        private int nextId = 2;

        public XElement Root => root;

        private static string FormatNumber(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

        public string AddGroup(string parent, double x, double y, double width, double height, string style)
        {
            var id = nextId++.ToString(CultureInfo.InvariantCulture);
            root.Add(new XElement("mxCell",
                new XAttribute("id", id),
                new XAttribute("parent", parent),
                new XAttribute("style", style),
                new XAttribute("value", string.Empty),
                new XAttribute("vertex", "1"),
                new XAttribute("connectable", "0"),
                new XElement("mxGeometry",
                    new XAttribute("x", FormatNumber(x)),
                    new XAttribute("y", FormatNumber(y)),
                    new XAttribute("width", FormatNumber(width)),
                    new XAttribute("height", FormatNumber(height)),
                    new XAttribute("as", "geometry"))));
            return id;
        }

        public void AddVertex(string parent, double x, double y, double width, double height, string style, string value)
        {
            var id = nextId++.ToString(CultureInfo.InvariantCulture);
            root.Add(new XElement("mxCell",
                new XAttribute("id", id),
                new XAttribute("parent", parent),
                new XAttribute("style", style),
                new XAttribute("value", value),
                new XAttribute("vertex", "1"),
                new XElement("mxGeometry",
                    new XAttribute("x", FormatNumber(x)),
                    new XAttribute("y", FormatNumber(y)),
                    new XAttribute("width", FormatNumber(width)),
                    new XAttribute("height", FormatNumber(height)),
                    new XAttribute("as", "geometry"))));
        }
    }

    private sealed class DiagramReportData
    {
        public required List<DiagramDomainNode> Domains { get; init; }
        public required int ProductCount { get; init; }
        public required int MappedProductCount { get; init; }
        public required List<DiagramProductNode> UnmappedProducts { get; init; }
    }

    private sealed record DiagramMetadata(
        string ScopeKey,
        int? ServiceId,
        int? ApplicationId,
        string ReportFragmentId,
        string DiagramTitle,
        string DiagramDescription,
        string PosterTitle,
        string PosterDescription,
        string MappedItemLabel,
        string EmptyStateTitle,
        string EmptyStateBody,
        string BackReportAction,
        string BackReportLabel,
        bool ShowUnmappedItems,
        bool ShowComponentMappedSummary,
        string UnmappedSectionTitle,
        string UnmappedSummaryLabel,
        string? DrawIoDownloadAction,
        string? ArchiDownloadAction);

    private sealed class DiagramDomainNode(int domainId, string code, string name)
    {
        public int DomainId { get; } = domainId;
        public string Code { get; } = code;
        public string Name { get; } = name;
        public string DisplayLabel => string.IsNullOrWhiteSpace(Code) ? Name : $"{Code} {Name}";
        public List<DiagramCapabilityNode> Capabilities { get; } = [];
    }

    private sealed class DiagramCapabilityNode(int capabilityId, string code, string name, DiagramDomainNode parentDomain)
    {
        public int CapabilityId { get; } = capabilityId;
        public string Code { get; } = code;
        public string Name { get; } = name;
        public DiagramDomainNode ParentDomain { get; } = parentDomain;
        public string DisplayLabel => string.IsNullOrWhiteSpace(Code) ? Name : $"{Code} {Name}";
        public List<DiagramComponentNode> Components { get; } = [];
    }

    private sealed class DiagramComponentNode(int componentId, string code, string name, DiagramCapabilityNode parentCapability)
    {
        public int ComponentId { get; } = componentId;
        public string Code { get; } = code;
        public string Name { get; } = name;
        public DiagramCapabilityNode ParentCapability { get; } = parentCapability;
        public string DisplayLabel => string.IsNullOrWhiteSpace(Code) ? Name : $"{Code} {Name}";
        public List<DiagramProductNode> Products { get; } = [];
    }

    private sealed record DiagramProductNode(
        int ProductId,
        string Name,
        string StatusLabel,
        string? Vendor,
        string? Version,
        string? OwnersLabel);

    private sealed record ResolvedPlacement(
        DiagramDomainNode Domain,
        DiagramCapabilityNode Capability,
        DiagramComponentNode Component,
        ProductMapping Mapping);

    private sealed record ExportDomainNode(
        string ElementId,
        string Code,
        string Name,
        IReadOnlyList<ExportCapabilityNode> Capabilities);

    private sealed record ExportCapabilityNode(
        string ElementId,
        string Code,
        string Name,
        IReadOnlyList<ExportComponentNode> Components)
    {
        public string DisplayLabel => string.IsNullOrWhiteSpace(Code) ? Name : $"{Code} {Name}";
    }

    private sealed record ExportComponentNode(
        string ElementId,
        string Code,
        string Name,
        IReadOnlyList<DiagramProductNode> Products,
        bool IsProductProxy)
    {
        public string DisplayLabel => string.IsNullOrWhiteSpace(Code) ? Name : $"{Code} {Name}";
    }

    private sealed record DiagramLayout(
        IReadOnlyList<DiagramDomainPlacement> Domains,
        double CanvasWidth,
        double CanvasHeight);

    private sealed record DiagramDomainPlacement(
        ExportDomainNode Domain,
        double X,
        double Y,
        double Width,
        double Height,
        IReadOnlyList<DiagramCapabilityPlacement> Capabilities);

    private sealed record DiagramCapabilityPlacement(
        ExportCapabilityNode Capability,
        double X,
        double Y,
        double Width,
        double Height,
        IReadOnlyList<DiagramComponentPlacement> Components);

    private sealed record DiagramComponentPlacement(
        ExportComponentNode Component,
        double X,
        double Y,
        double Width,
        double Height,
        double HeaderHeight,
        IReadOnlyList<DiagramProductPlacement> Products);

    private sealed record DiagramProductPlacement(
        DiagramProductNode Product,
        double X,
        double Y,
        double Width,
        double Height);
}
