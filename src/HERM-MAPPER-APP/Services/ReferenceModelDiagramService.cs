using HERMMapperApp.Data;
using HERMMapperApp.Models;
using HERMMapperApp.ViewModels;
using Microsoft.EntityFrameworkCore;

namespace HERMMapperApp.Services;

public sealed class ReferenceModelDiagramService(AppDbContext dbContext)
{
    public async Task<ModelDiagramReportViewModel> BuildArmAsync(CancellationToken cancellationToken = default)
    {
        var domains = await dbContext.ArmDomains
            .AsNoTracking()
            .OrderBy(x => x.Code)
            .ThenBy(x => x.Name)
            .ToListAsync(cancellationToken);

        var capabilities = await dbContext.ArmCapabilities
            .AsNoTracking()
            .OrderBy(x => x.ParentDomainId)
            .ThenBy(x => x.Code)
            .ThenBy(x => x.Name)
            .ToListAsync(cancellationToken);

        var components = await dbContext.ArmComponents
            .AsNoTracking()
            .Where(x => !x.IsDeleted)
            .OrderBy(x => x.ParentCapabilityId)
            .ThenBy(x => x.Code)
            .ThenBy(x => x.Name)
            .ToListAsync(cancellationToken);

        var mappings = await dbContext.ApplicationCatalogItemMappings
            .AsNoTracking()
            .Where(x => x.ApplicationCatalogItem != null && !x.ApplicationCatalogItem.IsDeleted)
            .Include(x => x.ArmComponent)
            .ThenInclude(x => x!.ParentCapability)
            .ThenInclude(x => x!.ParentDomain)
            .Include(x => x.ProductMapping)
            .ThenInclude(x => x!.TrmDomain)
            .Include(x => x.ProductMapping)
            .ThenInclude(x => x!.TrmCapability)
            .ThenInclude(x => x!.ParentDomain)
            .Include(x => x.ProductMapping)
            .ThenInclude(x => x!.TrmComponent)
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

        var placements = mappings
            .SelectMany(ResolveArmPlacements)
            .ToList();

        return BuildReport(
            domains.Select(x => new DiagramDomainSeed(x.Id, x.Code, x.Name)).ToList(),
            capabilities.Select(x => new DiagramCapabilitySeed(x.Id, x.ParentDomainId, x.Code, x.Name)).ToList(),
            components.Select(x => new DiagramComponentSeed(x.Id, x.ParentCapabilityId, x.Code, x.Name)).ToList(),
            placements,
            new DiagramMetadata(
                ScopeKey: "arm",
                BrmModelId: null,
                ServiceId: null,
                ApplicationId: null,
                ReportFragmentId: "report-arm-model",
                DiagramTitle: "ARM diagram (all objects)",
                DiagramDescription: "Browse the ARM structure with mapped TRM domains placed inside each component.",
                PosterTitle: "ARM model poster",
                PosterDescription: "Full-screen poster view of the ARM model with mapped TRM domains placed directly inside each component column.",
                MappedItemLabel: "mapped TRM domain(s)",
                BackReportAction: "ArmModelReport",
                BackReportLabel: "Back to ARM report",
                OnlyShowMappedNodes: false,
                UseCompactMappedSummary: true,
                ShowComponentMappedSummary: false,
                ShowBranchEmptyStates: false));
    }

    public async Task<ModelDiagramReportViewModel> BuildArmApplicationAsync(int? applicationId, CancellationToken cancellationToken = default)
    {
        var selectedApplication = applicationId is > 0
            ? await dbContext.ApplicationCatalogItems
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == applicationId.Value && !x.IsDeleted, cancellationToken)
            : null;

        if (selectedApplication is null)
        {
            return new ModelDiagramReportViewModel
            {
                ScopeKey = "arm",
                ReportFragmentId = "report-arm-application",
                DiagramTitle = "ARM diagram per application",
                DiagramDescription = "Choose an application to review the ARM structure touched by its mappings.",
                PosterTitle = "ARM application diagram poster",
                PosterDescription = "Full-screen poster view of the selected application across the ARM reference model.",
                MappedItemLabel = "mapped TRM domain(s)",
                EmptyStateTitle = "No application selected",
                EmptyStateBody = "Choose an application to build an ARM diagram for its mappings.",
                BackReportAction = "ArmApplicationDiagramReport",
                BackReportLabel = "Back to application ARM report",
                OnlyShowMappedNodes = true,
                UseCompactMappedSummary = true,
                ShowComponentMappedSummary = false,
                ShowBranchEmptyStates = false
            };
        }

        var domains = await dbContext.ArmDomains
            .AsNoTracking()
            .OrderBy(x => x.Code)
            .ThenBy(x => x.Name)
            .ToListAsync(cancellationToken);

        var capabilities = await dbContext.ArmCapabilities
            .AsNoTracking()
            .OrderBy(x => x.ParentDomainId)
            .ThenBy(x => x.Code)
            .ThenBy(x => x.Name)
            .ToListAsync(cancellationToken);

        var components = await dbContext.ArmComponents
            .AsNoTracking()
            .Where(x => !x.IsDeleted)
            .OrderBy(x => x.ParentCapabilityId)
            .ThenBy(x => x.Code)
            .ThenBy(x => x.Name)
            .ToListAsync(cancellationToken);

        var mappings = await dbContext.ApplicationCatalogItemMappings
            .AsNoTracking()
            .Where(x => x.ApplicationCatalogItemId == selectedApplication.Id)
            .Include(x => x.ArmComponent)
            .ThenInclude(x => x!.ParentCapability)
            .ThenInclude(x => x!.ParentDomain)
            .Include(x => x.ProductMapping)
            .ThenInclude(x => x!.TrmDomain)
            .Include(x => x.ProductMapping)
            .ThenInclude(x => x!.TrmCapability)
            .ThenInclude(x => x!.ParentDomain)
            .Include(x => x.ProductMapping)
            .ThenInclude(x => x!.TrmComponent)
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

        var placements = mappings
            .SelectMany(ResolveArmPlacements)
            .ToList();

        if (placements.Count == 0)
        {
            return new ModelDiagramReportViewModel
            {
                ScopeKey = "arm",
                ApplicationId = selectedApplication.Id,
                ReportFragmentId = "report-arm-application",
                DiagramTitle = "ARM diagram per application",
                DiagramDescription = BuildApplicationDiagramDescription(selectedApplication),
                PosterTitle = $"{selectedApplication.Name} ARM application poster",
                PosterDescription = $"Full-screen poster view of {selectedApplication.Name} across the ARM reference model.",
                MappedItemLabel = "mapped TRM domain(s)",
                EmptyStateTitle = $"No ARM mappings found for {selectedApplication.Name}",
                EmptyStateBody = "Add ARM mappings for this application to populate the report.",
                BackReportAction = "ArmApplicationDiagramReport",
                BackReportLabel = "Back to application ARM report",
                OnlyShowMappedNodes = true,
                UseCompactMappedSummary = true,
                ShowComponentMappedSummary = false,
                ShowBranchEmptyStates = false
            };
        }

        return BuildReport(
            domains.Select(x => new DiagramDomainSeed(x.Id, x.Code, x.Name)).ToList(),
            capabilities.Select(x => new DiagramCapabilitySeed(x.Id, x.ParentDomainId, x.Code, x.Name)).ToList(),
            components.Select(x => new DiagramComponentSeed(x.Id, x.ParentCapabilityId, x.Code, x.Name)).ToList(),
            placements,
            new DiagramMetadata(
                ScopeKey: "arm",
                BrmModelId: null,
                ServiceId: null,
                ApplicationId: selectedApplication.Id,
                ReportFragmentId: "report-arm-application",
                DiagramTitle: "ARM diagram per application",
                DiagramDescription: BuildApplicationDiagramDescription(selectedApplication),
                PosterTitle: $"{selectedApplication.Name} ARM application poster",
                PosterDescription: $"Full-screen poster view of {selectedApplication.Name} across the ARM reference model with only that application's resolved mappings shown.",
                MappedItemLabel: "mapped TRM domain(s)",
                BackReportAction: "ArmApplicationDiagramReport",
                BackReportLabel: "Back to application ARM report",
                OnlyShowMappedNodes: false,
                UseCompactMappedSummary: true,
                ShowComponentMappedSummary: false,
                ShowBranchEmptyStates: false,
                EmptyStateTitle: $"No ARM structure available for {selectedApplication.Name}",
                EmptyStateBody: "Import the ARM reference model and add application mappings to render this report."));
    }

    public async Task<ModelDiagramReportViewModel> BuildBrmAsync(CancellationToken cancellationToken = default)
    {
        var domains = await dbContext.BrmDomains
            .AsNoTracking()
            .OrderBy(x => x.Code)
            .ThenBy(x => x.Name)
            .ToListAsync(cancellationToken);

        var capabilities = await dbContext.BrmCapabilities
            .AsNoTracking()
            .OrderBy(x => x.ParentDomainId)
            .ThenBy(x => x.Code)
            .ThenBy(x => x.Name)
            .ToListAsync(cancellationToken);

        var components = await dbContext.BrmComponents
            .AsNoTracking()
            .Where(x => !x.IsDeleted)
            .OrderBy(x => x.ParentCapabilityId)
            .ThenBy(x => x.Code)
            .ThenBy(x => x.Name)
            .ToListAsync(cancellationToken);

        var mappings = await dbContext.BusinessCapabilityCatalogItemMappings
            .AsNoTracking()
            .Where(x =>
                x.BusinessCapabilityCatalogItem == null ||
                x.BusinessCapabilityCatalogItem.BrmModel == null ||
                !x.BusinessCapabilityCatalogItem.BrmModel.IsDeleted)
            .Include(x => x.BrmComponent)
            .ThenInclude(x => x!.ParentCapability)
            .ThenInclude(x => x!.ParentDomain)
            .Include(x => x.ArmCapability)
            .ThenInclude(x => x!.ParentDomain)
            .Include(x => x.ArmComponent)
            .ThenInclude(x => x!.ParentCapability)
            .ThenInclude(x => x!.ParentDomain)
            .AsSplitQuery()
            .ToListAsync(cancellationToken);

        var placements = mappings
            .SelectMany(ResolveBrmPlacements)
            .ToList();

        return BuildReport(
            domains.Select(x => new DiagramDomainSeed(x.Id, x.Code, x.Name)).ToList(),
            capabilities.Select(x => new DiagramCapabilitySeed(x.Id, x.ParentDomainId, x.Code, x.Name)).ToList(),
            components.Select(x => new DiagramComponentSeed(x.Id, x.ParentCapabilityId, x.Code, x.Name)).ToList(),
            placements,
            new DiagramMetadata(
                ScopeKey: "brm",
                BrmModelId: null,
                ServiceId: null,
                ApplicationId: null,
                ReportFragmentId: "report-brm-model",
                DiagramTitle: "BRM diagram",
                DiagramDescription: "Browse the BRM structure with mapped ARM domains placed inside each level 2 capability.",
                PosterTitle: "BRM model poster",
                PosterDescription: "Full-screen poster view of the BRM model with mapped ARM domains placed directly inside each level 2 capability column.",
                MappedItemLabel: "mapped ARM domain(s)",
                BackReportAction: "BrmModelReport",
                BackReportLabel: "Back to BRM report",
                OnlyShowMappedNodes: false,
                UseCompactMappedSummary: true,
                ShowComponentMappedSummary: false,
                ShowBranchEmptyStates: false));
    }

    public async Task<ModelDiagramReportViewModel> BuildBrmModelAsync(int? brmModelId, CancellationToken cancellationToken = default)
    {
        var selectedBrmModel = brmModelId is > 0
            ? await dbContext.BrmModels
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == brmModelId.Value && !x.IsDeleted, cancellationToken)
            : await dbContext.BrmModels
                .AsNoTracking()
                .Where(x => !x.IsDeleted)
                .OrderBy(x => x.Name)
                .ThenBy(x => x.Area)
                .FirstOrDefaultAsync(cancellationToken);

        if (selectedBrmModel is null)
        {
            return new ModelDiagramReportViewModel
            {
                ScopeKey = "brm",
                ReportFragmentId = "report-brm-model",
                DiagramTitle = "BRM diagram",
                DiagramDescription = "Choose one of your BRM models to review its mapped capability structure.",
                PosterTitle = "BRM model poster",
                PosterDescription = "Full-screen poster view of the selected BRM model.",
                MappedItemLabel = "mapped ARM domain(s)",
                EmptyStateTitle = "No BRM models available",
                EmptyStateBody = "Create a BRM model and add capabilities to populate this report.",
                BackReportAction = "BrmModelReport",
                BackReportLabel = "Back to BRM report",
                OnlyShowMappedNodes = true,
                UseCompactMappedSummary = true,
                ShowComponentMappedSummary = false,
                ShowBranchEmptyStates = false
            };
        }

        var domains = await dbContext.BrmDomains
            .AsNoTracking()
            .OrderBy(x => x.Code)
            .ThenBy(x => x.Name)
            .ToListAsync(cancellationToken);

        var capabilities = await dbContext.BrmCapabilities
            .AsNoTracking()
            .OrderBy(x => x.ParentDomainId)
            .ThenBy(x => x.Code)
            .ThenBy(x => x.Name)
            .ToListAsync(cancellationToken);

        var components = await dbContext.BrmComponents
            .AsNoTracking()
            .Where(x => !x.IsDeleted)
            .OrderBy(x => x.ParentCapabilityId)
            .ThenBy(x => x.Code)
            .ThenBy(x => x.Name)
            .ToListAsync(cancellationToken);

        var mappings = await dbContext.BusinessCapabilityCatalogItemMappings
            .AsNoTracking()
            .Where(x => x.BusinessCapabilityCatalogItem != null && x.BusinessCapabilityCatalogItem.BrmModelId == selectedBrmModel.Id)
            .Include(x => x.BrmComponent)
            .ThenInclude(x => x!.ParentCapability)
            .ThenInclude(x => x!.ParentDomain)
            .Include(x => x.ArmCapability)
            .ThenInclude(x => x!.ParentDomain)
            .Include(x => x.ArmComponent)
            .ThenInclude(x => x!.ParentCapability)
            .ThenInclude(x => x!.ParentDomain)
            .AsSplitQuery()
            .ToListAsync(cancellationToken);

        var report = BuildReport(
            domains.Select(x => new DiagramDomainSeed(x.Id, x.Code, x.Name)).ToList(),
            capabilities.Select(x => new DiagramCapabilitySeed(x.Id, x.ParentDomainId, x.Code, x.Name)).ToList(),
            components.Select(x => new DiagramComponentSeed(x.Id, x.ParentCapabilityId, x.Code, x.Name)).ToList(),
            mappings.SelectMany(ResolveBrmPlacements).ToList(),
            new DiagramMetadata(
                ScopeKey: "brm",
                BrmModelId: selectedBrmModel.Id,
                ServiceId: null,
                ApplicationId: null,
                ReportFragmentId: "report-brm-model",
                DiagramTitle: "BRM diagram",
                DiagramDescription: BuildBrmModelDescription(selectedBrmModel),
                PosterTitle: $"{selectedBrmModel.Name} BRM model poster",
                PosterDescription: $"Full-screen poster view of {selectedBrmModel.Name} across the full BRM reference model with mapped ARM domains placed where they exist.",
                MappedItemLabel: "mapped ARM domain(s)",
                BackReportAction: "BrmModelReport",
                BackReportLabel: "Back to BRM report",
                OnlyShowMappedNodes: false,
                UseCompactMappedSummary: true,
                ShowComponentMappedSummary: false,
                ShowBranchEmptyStates: false,
                EmptyStateTitle: $"No BRM structure available for {selectedBrmModel.Name}",
                EmptyStateBody: "Import the BRM reference model to render the full poster and add mappings to populate the ARM placement summary."));

        return report;
    }

    public async Task<ModelDiagramReportViewModel> BuildDrmModelAsync(int? drmModelId, CancellationToken cancellationToken = default)
    {
        var selectedDrmModel = drmModelId is > 0
            ? await dbContext.DrmModels
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == drmModelId.Value && !x.IsDeleted, cancellationToken)
            : await dbContext.DrmModels
                .AsNoTracking()
                .Where(x => !x.IsDeleted)
                .OrderBy(x => x.Name)
                .ThenBy(x => x.Area)
                .FirstOrDefaultAsync(cancellationToken);

        if (selectedDrmModel is null)
        {
            return new ModelDiagramReportViewModel
            {
                ScopeKey = "drm",
                ReportFragmentId = "report-drm-model",
                DiagramTitle = "DRM diagram",
                DiagramDescription = "Choose one of your DRM models to review its data-reference structure.",
                PosterTitle = "DRM model poster",
                PosterDescription = "Full-screen poster view of the selected DRM model.",
                MappedItemLabel = "selected record(s)",
                EmptyStateTitle = "No DRM models available",
                EmptyStateBody = "Create a DRM model and add data entities to populate this report.",
                BackReportAction = "DrmModelReport",
                BackReportLabel = "Back to DRM report",
                OnlyShowMappedNodes = false,
                UseCompactMappedSummary = true,
                ShowComponentMappedSummary = false,
                ShowBranchEmptyStates = false
            };
        }

        var topicTypes = await dbContext.DrmTopicTypes
            .AsNoTracking()
            .OrderBy(x => x.Code)
            .ThenBy(x => x.Name)
            .ToListAsync(cancellationToken);
        var topics = await dbContext.DrmTopics
            .AsNoTracking()
            .OrderBy(x => x.TopicTypeId)
            .ThenBy(x => x.Code)
            .ThenBy(x => x.Name)
            .ToListAsync(cancellationToken);
        var entities = await dbContext.DrmEntities
            .AsNoTracking()
            .Where(x => !x.IsDeleted)
            .OrderBy(x => x.ParentTopicId)
            .ThenBy(x => x.Code)
            .ThenBy(x => x.Name)
            .ToListAsync(cancellationToken);
        var subClasses = await dbContext.DrmCommonSubClasses
            .AsNoTracking()
            .Include(x => x.ParentEntity)
            .Where(x => !x.IsDeleted && x.ParentEntity != null && !x.ParentEntity.IsDeleted)
            .OrderBy(x => x.ParentEntityId)
            .ThenBy(x => x.Code)
            .ThenBy(x => x.Name)
            .ToListAsync(cancellationToken);
        var selectedItems = await dbContext.DrmModelDataEntities
            .AsNoTracking()
            .Where(x => x.DrmModelId == selectedDrmModel.Id)
            .Include(x => x.DrmEntity)
            .Include(x => x.DrmCommonSubClass)
            .ToListAsync(cancellationToken);

        var componentSeeds = entities
            .Select(x => new DiagramComponentSeed(x.Id, x.ParentTopicId, x.Code, x.Name))
            .Concat(subClasses.Select(x => new DiagramComponentSeed(
                -x.Id,
                x.ParentEntity?.ParentTopicId,
                x.Code,
                x.Name)))
            .ToList();

        var placements = selectedItems
            .Select(x => new DiagramLeafPlacement(
                x.DrmCommonSubClassId.HasValue ? -x.DrmCommonSubClassId.Value : x.DrmEntityId,
                x.Id,
                x.Name,
                "DrmDataEntities",
                "Edit",
                x.Id))
            .ToList();

        return BuildReport(
            topicTypes.Select(x => new DiagramDomainSeed(x.Id, x.Code, x.Name)).ToList(),
            topics.Select(x => new DiagramCapabilitySeed(x.Id, x.TopicTypeId, x.Code, x.Name)).ToList(),
            componentSeeds,
            placements,
            new DiagramMetadata(
                ScopeKey: "drm",
                BrmModelId: null,
                ServiceId: null,
                ApplicationId: null,
                ReportFragmentId: "report-drm-model",
                DiagramTitle: "DRM diagram",
                DiagramDescription: BuildDrmModelDescription(selectedDrmModel),
                PosterTitle: $"{selectedDrmModel.Name} DRM model poster",
                PosterDescription: $"Full-screen poster view of {selectedDrmModel.Name} across the DRM reference model with selected data entities highlighted.",
                MappedItemLabel: "selected record(s)",
                BackReportAction: "DrmModelReport",
                BackReportLabel: "Back to DRM report",
                OnlyShowMappedNodes: false,
                UseCompactMappedSummary: true,
                ShowComponentMappedSummary: false,
                ShowBranchEmptyStates: false,
                EmptyStateTitle: $"No DRM structure available for {selectedDrmModel.Name}",
                EmptyStateBody: "Import the DRM reference model and add data entities to populate the selected-record summary.",
                DrmModelId: selectedDrmModel.Id));
    }

    private static IEnumerable<DiagramLeafPlacement> ResolveArmPlacements(ApplicationCatalogItemMapping mapping)
    {
        if (mapping.ArmComponentId <= 0)
        {
            yield break;
        }

        foreach (var productMapping in ResolveProductMappings(mapping))
        {
            var trmDomain = ResolveTrmDomain(productMapping);
            if (trmDomain is null)
            {
                continue;
            }

            yield return new DiagramLeafPlacement(
                mapping.ArmComponentId,
                trmDomain.Id,
                BuildLabel(trmDomain.Code, trmDomain.Name));
        }
    }

    private static IEnumerable<DiagramLeafPlacement> ResolveBrmPlacements(BusinessCapabilityCatalogItemMapping mapping)
    {
        if (mapping.BrmComponentId <= 0)
        {
            yield break;
        }

        var armDomain = mapping.ArmComponent?.ParentCapability?.ParentDomain ?? mapping.ArmCapability?.ParentDomain;
        if (armDomain is null)
        {
            yield break;
        }

        yield return new DiagramLeafPlacement(
            mapping.BrmComponentId,
            armDomain.Id,
            BuildLabel(armDomain.Code, armDomain.Name));
    }

    private static List<ProductMapping> ResolveProductMappings(ApplicationCatalogItemMapping mapping)
    {
        if (mapping.ProductMapping is not null)
        {
            return [mapping.ProductMapping];
        }

        return mapping.ProductCatalogItem?.Mappings?.ToList() ?? [];
    }

    private static TrmDomain? ResolveTrmDomain(ProductMapping mapping)
    {
        var capability = mapping.TrmComponent?.ParentCapability ?? mapping.TrmCapability;
        return mapping.TrmComponent?.ParentCapability?.ParentDomain ?? capability?.ParentDomain ?? mapping.TrmDomain;
    }

    private static string BuildLabel(string code, string name) =>
        string.IsNullOrWhiteSpace(code) ? name : $"{code} {name}";

    private static string BuildBrmModelDescription(BrmModel brmModel)
    {
        var areaLabel = string.IsNullOrWhiteSpace(brmModel.Area) ? brmModel.Name : $"{brmModel.Name} - {brmModel.Area}";
        return $"Review {areaLabel} across the full BRM poster with mapped ARM domains shown inside the configured BRM capabilities.";
    }

    private static string BuildDrmModelDescription(DrmModel drmModel)
    {
        var areaLabel = string.IsNullOrWhiteSpace(drmModel.Area) ? drmModel.Name : $"{drmModel.Name} - {drmModel.Area}";
        return $"Review {areaLabel} across the full DRM poster with selected data entities shown inside the configured DRM model.";
    }

    private static string BuildApplicationDiagramDescription(ApplicationCatalogItem application) =>
        $"Review {application.Name} across the ARM reference model with only that application's resolved TRM mappings shown in the mapped ARM components.";

    private static ModelDiagramReportViewModel BuildReport(
        IReadOnlyList<DiagramDomainSeed> domains,
        IReadOnlyList<DiagramCapabilitySeed> capabilities,
        IReadOnlyList<DiagramComponentSeed> components,
        IReadOnlyList<DiagramLeafPlacement> placements,
        DiagramMetadata metadata)
    {
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

            var capabilityNode = new DiagramCapabilityNode(capability.Id, capability.Code, capability.Name);
            domainNode.Capabilities.Add(capabilityNode);
            capabilitiesById[capability.Id] = capabilityNode;
        }

        foreach (var component in components)
        {
            if (component.ParentCapabilityId is not int capabilityId || !capabilitiesById.TryGetValue(capabilityId, out var capabilityNode))
            {
                continue;
            }

            var componentNode = new DiagramComponentNode(component.Id, component.Code, component.Name);
            capabilityNode.Components.Add(componentNode);
            componentsById[component.Id] = componentNode;
        }

        var seenPlacements = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var placement in placements
                     .OrderBy(x => x.ComponentId)
                     .ThenBy(x => x.Label, StringComparer.OrdinalIgnoreCase))
        {
            if (!componentsById.TryGetValue(placement.ComponentId, out var componentNode))
            {
                continue;
            }

            if (!seenPlacements.Add($"{placement.ComponentId}:{placement.LeafId}:{placement.Label}"))
            {
                continue;
            }

            componentNode.Products.Add(new ModelDiagramProductViewModel
            {
                ProductId = placement.LeafId,
                Name = placement.Label,
                StatusLabel = string.Empty,
                StatusCssClass = string.Empty,
                LinkController = placement.LinkController,
                LinkAction = placement.LinkAction,
                LinkId = placement.LinkId
            });
        }

        foreach (var component in domainNodes.SelectMany(x => x.Capabilities).SelectMany(x => x.Components))
        {
            component.Products.Sort((left, right) => StringComparer.OrdinalIgnoreCase.Compare(left.Name, right.Name));
        }

        var mappedItemCount = placements
            .Select(x => x.LeafId)
            .Distinct()
            .Count();

        return new ModelDiagramReportViewModel
        {
            ScopeKey = metadata.ScopeKey,
            BrmModelId = metadata.BrmModelId,
            DrmModelId = metadata.DrmModelId,
            ServiceId = metadata.ServiceId,
            ApplicationId = metadata.ApplicationId,
            ReportFragmentId = metadata.ReportFragmentId,
            DiagramTitle = metadata.DiagramTitle,
            DiagramDescription = metadata.DiagramDescription,
            PosterTitle = metadata.PosterTitle,
            PosterDescription = metadata.PosterDescription,
            MappedItemLabel = metadata.MappedItemLabel,
            EmptyStateTitle = metadata.EmptyStateTitle ?? "No model content available",
            EmptyStateBody = metadata.EmptyStateBody ?? "Import the matching reference model and mappings to populate this report.",
            BackReportAction = metadata.BackReportAction,
            BackReportLabel = metadata.BackReportLabel,
            ShowUnmappedItems = false,
            OnlyShowMappedNodes = metadata.OnlyShowMappedNodes,
            UseCompactMappedSummary = metadata.UseCompactMappedSummary,
            ShowComponentMappedSummary = metadata.ShowComponentMappedSummary,
            ShowBranchEmptyStates = metadata.ShowBranchEmptyStates,
            DrawIoDownloadAction = metadata.DrawIoDownloadAction,
            ArchiDownloadAction = metadata.ArchiDownloadAction,
            DomainCount = domainNodes.Count,
            CapabilityCount = domainNodes.Sum(x => x.Capabilities.Count),
            ComponentCount = domainNodes.Sum(x => x.Capabilities.Sum(capability => capability.Components.Count)),
            ProductCount = mappedItemCount,
            MappedProductCount = mappedItemCount,
            UnmappedProductCount = 0,
            ItemCount = mappedItemCount,
            MappedItemCount = mappedItemCount,
            UnmappedItemCount = 0,
            Domains = domainNodes.Select(MapDomain).ToList(),
            UnmappedProducts = []
        };
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
            Products = component.Products
        };

    private sealed record DiagramMetadata(
        string ScopeKey,
        int? BrmModelId,
        int? ServiceId,
        int? ApplicationId,
        string ReportFragmentId,
        string DiagramTitle,
        string DiagramDescription,
        string PosterTitle,
        string PosterDescription,
        string MappedItemLabel,
        string BackReportAction,
        string BackReportLabel,
        bool OnlyShowMappedNodes,
        bool UseCompactMappedSummary,
        bool ShowComponentMappedSummary,
        bool ShowBranchEmptyStates,
        string? EmptyStateTitle = null,
        string? EmptyStateBody = null,
        string? DrawIoDownloadAction = "DownloadDrawIo",
        string? ArchiDownloadAction = "DownloadArchiXml",
        int? DrmModelId = null);

    private sealed record DiagramDomainSeed(int Id, string Code, string Name);
    private sealed record DiagramCapabilitySeed(int Id, int? ParentDomainId, string Code, string Name);
    private sealed record DiagramComponentSeed(int Id, int? ParentCapabilityId, string Code, string Name);
    private sealed record DiagramLeafPlacement(int ComponentId, int LeafId, string Label, string? LinkController = null, string? LinkAction = null, int? LinkId = null);

    private sealed class DiagramDomainNode(int domainId, string code, string name)
    {
        public int DomainId { get; } = domainId;
        public string Code { get; } = code;
        public string Name { get; } = name;
        public List<DiagramCapabilityNode> Capabilities { get; } = [];
    }

    private sealed class DiagramCapabilityNode(int capabilityId, string code, string name)
    {
        public int CapabilityId { get; } = capabilityId;
        public string Code { get; } = code;
        public string Name { get; } = name;
        public List<DiagramComponentNode> Components { get; } = [];
    }

    private sealed class DiagramComponentNode(int componentId, string code, string name)
    {
        public int ComponentId { get; } = componentId;
        public string Code { get; } = code;
        public string Name { get; } = name;
        public List<ModelDiagramProductViewModel> Products { get; } = [];
    }
}
