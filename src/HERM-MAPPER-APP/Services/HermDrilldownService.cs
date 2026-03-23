using HERMMapperApp.Data;
using HERMMapperApp.Models;
using HERMMapperApp.ViewModels;
using Microsoft.EntityFrameworkCore;

namespace HERMMapperApp.Services;

public sealed class HermDrilldownService(AppDbContext dbContext)
{
    public async Task<ApplicationDetailsViewModel?> BuildApplicationDetailsAsync(int applicationId, CancellationToken cancellationToken = default)
    {
        var application = await BuildApplicationDetailsQuery()
            .FirstOrDefaultAsync(x => x.Id == applicationId, cancellationToken);

        if (application is null)
        {
            return null;
        }

        var mappingRows = application.Mappings
            .OrderBy(x => x.ArmComponent!.Code)
            .ThenBy(x => BuildProductLabel(GetMappedProduct(x)))
            .Select(mapping =>
            {
                var resolvedProductMapping = GetResolvedProductMappings(mapping).FirstOrDefault();
                return new ApplicationMappingRowViewModel
                {
                    ArmDomainLabel = BuildArmDomainLabel(mapping.ArmComponent),
                    ArmCapabilityLabel = BuildArmCapabilityLabel(mapping.ArmComponent),
                    ArmComponentLabel = mapping.ArmComponent?.DisplayLabel ?? "-",
                    ProductLabel = BuildProductLabel(GetMappedProduct(mapping)),
                    TrmDomainLabel = resolvedProductMapping is null ? "-" : BuildTrmDomainLabel(resolvedProductMapping),
                    TrmCapabilityLabel = resolvedProductMapping is null ? "-" : BuildTrmCapabilityLabel(resolvedProductMapping),
                    TrmComponentLabel = resolvedProductMapping?.TrmComponent?.DisplayLabel ?? "-",
                    MappingStatus = resolvedProductMapping?.MappingStatus.ToString() ?? "-"
                };
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
            HierarchyRoot = BuildApplicationHierarchy(application, resolvedPaths),
            GraphConnections = BuildApplicationGraphConnections(application, resolvedPaths),
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

    public async Task<ApplicationHierarchyNodeViewModel> BuildAllApplicationsHierarchyAsync(CancellationToken cancellationToken = default)
    {
        var applications = await BuildApplicationDetailsQuery()
            .OrderBy(x => x.Name)
            .ToListAsync(cancellationToken);

        var applicationNodes = applications
            .Select(application =>
            {
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

                return BuildApplicationHierarchy(application, resolvedPaths);
            })
            .ToList();

        return new ApplicationHierarchyNodeViewModel
        {
            Key = "applications-root",
            NodeType = "Applications",
            CssType = "application",
            Label = "All applications",
            PathCount = applicationNodes.Sum(x => x.PathCount),
            ProductCount = applicationNodes.Sum(x => x.ProductCount),
            IsExpanded = true,
            Children = applicationNodes
        };
    }

    public async Task<CapabilityDetailsViewModel?> BuildCapabilityDetailsAsync(int capabilityId, CancellationToken cancellationToken = default)
    {
        var capability = await BuildCapabilityDetailsQuery()
            .FirstOrDefaultAsync(x => x.Id == capabilityId, cancellationToken);

        if (capability is null)
        {
            return null;
        }

        var armComponentIds = capability.Mappings
            .Select(x => x.ArmComponentId)
            .Distinct()
            .ToList();

        var applicationMappings = await LoadApplicationMappingsForArmComponentsAsync(armComponentIds, cancellationToken);

        var applicationCountByArmComponent = applicationMappings
            .GroupBy(x => x.ArmComponentId)
            .ToDictionary(
                group => group.Key,
                group => group.Select(x => x.ApplicationCatalogItemId).Distinct().Count());

        var mappingRows = capability.Mappings
            .OrderBy(x => x.BrmComponent!.Code)
            .ThenBy(x => x.ArmComponent!.Code)
            .Select(mapping => new CapabilityMappingRowViewModel
            {
                BrmDomainLabel = BuildBrmDomainLabel(mapping.BrmComponent),
                BrmCapabilityLabel = BuildBrmCapabilityLabel(mapping.BrmComponent),
                BrmComponentLabel = mapping.BrmComponent?.DisplayLabel ?? "-",
                ArmDomainLabel = BuildArmDomainLabel(GetResolvedArmCapability(mapping)),
                ArmCapabilityLabel = BuildArmCapabilityLabel(GetResolvedArmCapability(mapping)),
                ArmComponentLabel = mapping.ArmComponent?.DisplayLabel ?? "-",
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
            BrmModelId = capability.BrmModelId,
            BrmModelName = capability.BrmModel?.Name ?? "-",
            BrmModelArea = capability.BrmModel?.Area ?? "-",
            BrmModelStatus = capability.BrmModel?.Status ?? "-",
            Name = capability.Name,
            Description = capability.Description,
            Notes = capability.Notes,
            UpdatedUtc = capability.UpdatedUtc,
            MappingRows = mappingRows,
            ResolvedPaths = resolvedPaths,
            HierarchyRoot = BuildCapabilityHierarchy(capability, resolvedPaths),
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

    public async Task<ApplicationHierarchyNodeViewModel> BuildAllCapabilitiesHierarchyAsync(CancellationToken cancellationToken = default)
    {
        var capabilities = await BuildCapabilityDetailsQuery()
            .OrderBy(x => x.Name)
            .ToListAsync(cancellationToken);

        var armComponentIds = capabilities
            .SelectMany(x => x.Mappings)
            .Select(x => x.ArmComponentId)
            .Distinct()
            .ToList();

        var applicationMappings = await LoadApplicationMappingsForArmComponentsAsync(armComponentIds, cancellationToken);

        var capabilityNodes = capabilities
            .Select(capability =>
            {
                var resolvedPaths = capability.Mappings
                    .SelectMany(mapping => BuildCapabilityPaths(
                        mapping,
                        applicationMappings.Where(x => x.ArmComponentId == mapping.ArmComponentId),
                        includeMappingOnlyBranch: true))
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

                return BuildCapabilityHierarchy(capability, resolvedPaths);
            })
            .ToList();

        return new ApplicationHierarchyNodeViewModel
        {
            Key = "capabilities-root",
            NodeType = "Capabilities",
            CssType = "capability",
            Label = "All capabilities",
            PathCount = capabilityNodes.Sum(x => x.PathCount),
            ProductCount = capabilityNodes.Sum(x => x.ProductCount),
            IsExpanded = true,
            Children = capabilityNodes
        };
    }

    public async Task<ApplicationHierarchyNodeViewModel> BuildBrmModelHierarchyAsync(int brmModelId, CancellationToken cancellationToken = default)
    {
        var brmModel = await dbContext.BrmModels
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == brmModelId, cancellationToken);

        if (brmModel is null)
        {
            return new ApplicationHierarchyNodeViewModel
            {
                Key = $"brm-model-{brmModelId}",
                NodeType = "BRM model",
                CssType = "capability",
                Label = "BRM model",
                IsExpanded = true
            };
        }

        var capabilities = await BuildCapabilityDetailsQuery()
            .Where(x => x.BrmModelId == brmModelId)
            .OrderBy(x => x.Name)
            .ToListAsync(cancellationToken);

        var armComponentIds = capabilities
            .SelectMany(x => x.Mappings)
            .Select(x => x.ArmComponentId)
            .Distinct()
            .ToList();

        var applicationMappings = await LoadApplicationMappingsForArmComponentsAsync(armComponentIds, cancellationToken);
        var allResolvedPaths = new List<CapabilityResolvedPathViewModel>();

        var capabilityNodes = capabilities
            .Select(capability =>
            {
                var resolvedPaths = capability.Mappings
                    .SelectMany(mapping => BuildCapabilityPaths(
                        mapping,
                        applicationMappings.Where(x => x.ArmComponentId == mapping.ArmComponentId),
                        includeMappingOnlyBranch: true))
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

                allResolvedPaths.AddRange(resolvedPaths);
                return BuildCapabilityHierarchy(capability, resolvedPaths);
            })
            .ToList();

        return new ApplicationHierarchyNodeViewModel
        {
            Key = $"brm-model-{brmModelId}",
            NodeType = "BRM model",
            CssType = "capability",
            Label = brmModel.Name,
            PathCount = capabilityNodes.Sum(x => x.PathCount),
            ProductCount = CountDistinctProducts(allResolvedPaths),
            IsExpanded = true,
            Children = capabilityNodes
        };
    }

    private IQueryable<ApplicationCatalogItem> BuildApplicationDetailsQuery() =>
        dbContext.ApplicationCatalogItems
            .AsNoTracking()
            .Include(x => x.Mappings)
            .ThenInclude(x => x.ArmComponent)
            .ThenInclude(x => x!.ParentCapability)
            .ThenInclude(x => x!.ParentDomain)
            .Include(x => x.Mappings)
            .ThenInclude(x => x.ProductMapping)
            .ThenInclude(x => x!.ProductCatalogItem)
            .Include(x => x.Mappings)
            .ThenInclude(x => x.ProductMapping)
            .ThenInclude(x => x!.TrmDomain)
            .Include(x => x.Mappings)
            .ThenInclude(x => x.ProductMapping)
            .ThenInclude(x => x!.TrmCapability)
            .ThenInclude(x => x!.ParentDomain)
            .Include(x => x.Mappings)
            .ThenInclude(x => x.ProductMapping)
            .ThenInclude(x => x!.TrmComponent)
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
            .AsSplitQuery();

    private IQueryable<BusinessCapabilityCatalogItem> BuildCapabilityDetailsQuery() =>
        dbContext.BusinessCapabilityCatalogItems
            .AsNoTracking()
            .Include(x => x.BrmModel)
            .Include(x => x.Mappings)
            .ThenInclude(x => x.BrmComponent)
            .ThenInclude(x => x!.ParentCapability)
            .ThenInclude(x => x!.ParentDomain)
            .Include(x => x.Mappings)
            .ThenInclude(x => x.ArmCapability)
            .ThenInclude(x => x!.ParentDomain)
            .Include(x => x.Mappings)
            .ThenInclude(x => x.ArmComponent)
            .ThenInclude(x => x!.ParentCapability)
            .ThenInclude(x => x!.ParentDomain)
            .AsSplitQuery();

    private async Task<List<ApplicationCatalogItemMapping>> LoadApplicationMappingsForArmComponentsAsync(
        List<int> armComponentIds,
        CancellationToken cancellationToken)
    {
        if (armComponentIds.Count == 0)
        {
            return [];
        }

        return await dbContext.ApplicationCatalogItemMappings
            .AsNoTracking()
            .Where(x => armComponentIds.Contains(x.ArmComponentId))
            .Include(x => x.ApplicationCatalogItem)
            .Include(x => x.ArmComponent)
            .ThenInclude(x => x!.ParentCapability)
            .ThenInclude(x => x!.ParentDomain)
            .Include(x => x.ProductMapping)
            .ThenInclude(x => x!.ProductCatalogItem)
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
    }

    private static IEnumerable<ApplicationResolvedPathViewModel> BuildApplicationPaths(ApplicationCatalogItemMapping mapping)
    {
        var armDomainLabel = BuildArmDomainLabel(mapping.ArmComponent);
        var armCapabilityLabel = BuildArmCapabilityLabel(mapping.ArmComponent);
        var armComponentLabel = mapping.ArmComponent?.DisplayLabel ?? "-";
        var productLabel = BuildProductLabel(GetMappedProduct(mapping));
        var productMappings = GetResolvedProductMappings(mapping);

        if (productMappings.Count == 0)
        {
            yield return new ApplicationResolvedPathViewModel
            {
                ArmDomainLabel = armDomainLabel,
                ArmCapabilityLabel = armCapabilityLabel,
                ArmComponentLabel = armComponentLabel,
                ProductLabel = productLabel,
                ProductId = GetMappedProduct(mapping)?.Id
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
                ProductLabel = BuildProductLabel(productMapping.ProductCatalogItem ?? GetMappedProduct(mapping)),
                ProductId = productMapping.ProductCatalogItem?.Id ?? GetMappedProduct(mapping)?.Id,
                TrmDomainLabel = BuildTrmDomainLabel(productMapping),
                TrmCapabilityLabel = BuildTrmCapabilityLabel(productMapping),
                TrmComponentLabel = productMapping.TrmComponent?.DisplayLabel ?? "-",
                MappingStatus = productMapping.MappingStatus.ToString()
            };
        }
    }

    private static IEnumerable<CapabilityResolvedPathViewModel> BuildCapabilityPaths(
        BusinessCapabilityCatalogItemMapping mapping,
        IEnumerable<ApplicationCatalogItemMapping> applicationMappings,
        bool includeMappingOnlyBranch = false)
    {
        var brmDomainLabel = BuildBrmDomainLabel(mapping.BrmComponent);
        var brmCapabilityLabel = BuildBrmCapabilityLabel(mapping.BrmComponent);
        var brmComponentLabel = mapping.BrmComponent?.DisplayLabel ?? "-";
        var armCapability = GetResolvedArmCapability(mapping);
        var armDomainLabel = BuildArmDomainLabel(armCapability);
        var armCapabilityLabel = BuildArmCapabilityLabel(armCapability);
        var armComponentLabel = mapping.ArmComponent?.DisplayLabel ?? "-";
        var applicationMappingList = applicationMappings.ToList();

        if (applicationMappingList.Count == 0)
        {
            if (includeMappingOnlyBranch)
            {
                yield return new CapabilityResolvedPathViewModel
                {
                    BrmDomainLabel = brmDomainLabel,
                    BrmCapabilityLabel = brmCapabilityLabel,
                    BrmComponentLabel = brmComponentLabel,
                    ArmDomainLabel = armDomainLabel,
                    ArmCapabilityLabel = armCapabilityLabel,
                    ArmComponentLabel = armComponentLabel
                };
            }

            yield break;
        }

        foreach (var applicationMapping in applicationMappingList)
        {
            var productMappings = GetResolvedProductMappings(applicationMapping);
            if (productMappings.Count == 0)
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
                    ProductLabel = BuildProductLabel(GetMappedProduct(applicationMapping)),
                    ProductId = GetMappedProduct(applicationMapping)?.Id
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
                    ProductLabel = BuildProductLabel(productMapping.ProductCatalogItem ?? GetMappedProduct(applicationMapping)),
                    ProductId = productMapping.ProductCatalogItem?.Id ?? GetMappedProduct(applicationMapping)?.Id,
                    TrmDomainLabel = BuildTrmDomainLabel(productMapping),
                    TrmCapabilityLabel = BuildTrmCapabilityLabel(productMapping),
                    TrmComponentLabel = productMapping.TrmComponent?.DisplayLabel ?? "-",
                    MappingStatus = productMapping.MappingStatus.ToString()
                };
            }
        }
    }

    private static ApplicationHierarchyNodeViewModel BuildCapabilityHierarchy(
        BusinessCapabilityCatalogItem capability,
        List<CapabilityResolvedPathViewModel> resolvedPaths) =>
        new()
        {
            Key = $"capability-{capability.Id}",
            NodeType = "Capability",
            CssType = "capability",
            Label = capability.Name,
            PathCount = resolvedPaths.Count,
            ProductCount = CountDistinctProducts(resolvedPaths),
            IsExpanded = true,
            Children = BuildCapabilityHierarchyNodes(resolvedPaths, 0, $"capability-{capability.Id}")
        };

    private static List<ProductMapping> GetResolvedProductMappings(ApplicationCatalogItemMapping mapping)
    {
        if (mapping.ProductMapping is not null)
        {
            return [mapping.ProductMapping];
        }

        return mapping.ProductCatalogItem?.Mappings?.ToList() ?? [];
    }

    private static ProductCatalogItem? GetMappedProduct(ApplicationCatalogItemMapping mapping) =>
        mapping.ProductMapping?.ProductCatalogItem ?? mapping.ProductCatalogItem;

    private static ApplicationHierarchyNodeViewModel BuildApplicationHierarchy(
        ApplicationCatalogItem application,
        List<ApplicationResolvedPathViewModel> resolvedPaths) =>
        new()
        {
            Key = $"application-{application.Id}",
            NodeType = "Application",
            CssType = "application",
            Label = application.Name,
            PathCount = resolvedPaths.Count,
            ProductCount = CountDistinctProducts(resolvedPaths),
            IsExpanded = true,
            Children = BuildApplicationHierarchyNodes(resolvedPaths, 0, $"application-{application.Id}")
        };

    private static List<ApplicationGraphConnectionViewModel> BuildApplicationGraphConnections(
        ApplicationCatalogItem application,
        List<ApplicationResolvedPathViewModel> resolvedPaths)
    {
        if (resolvedPaths.Count == 0)
        {
            return [];
        }

        var connections = new List<ApplicationGraphConnectionViewModel>();
        var seenEdges = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var applicationNode = new ApplicationGraphNode(
            $"application:{application.Id}",
            string.IsNullOrWhiteSpace(application.Name) ? $"Application {application.Id}" : application.Name);

        foreach (var path in resolvedPaths)
        {
            var nodes = new[]
            {
                applicationNode,
                new ApplicationGraphNode(
                    $"arm-domain:{NormalizeHierarchyLabel(path.ArmDomainLabel, "ARM domain")}",
                    NormalizeHierarchyLabel(path.ArmDomainLabel, "ARM domain")),
                new ApplicationGraphNode(
                    $"arm-capability:{NormalizeHierarchyLabel(path.ArmCapabilityLabel, "ARM capability")}",
                    NormalizeHierarchyLabel(path.ArmCapabilityLabel, "ARM capability")),
                new ApplicationGraphNode(
                    $"arm-component:{NormalizeHierarchyLabel(path.ArmComponentLabel, "ARM component")}",
                    NormalizeHierarchyLabel(path.ArmComponentLabel, "ARM component")),
                new ApplicationGraphNode(
                    $"trm-domain:{NormalizeHierarchyLabel(path.TrmDomainLabel, "TRM domain")}",
                    NormalizeHierarchyLabel(path.TrmDomainLabel, "TRM domain")),
                new ApplicationGraphNode(
                    $"trm-capability:{NormalizeHierarchyLabel(path.TrmCapabilityLabel, "TRM capability")}",
                    NormalizeHierarchyLabel(path.TrmCapabilityLabel, "TRM capability")),
                new ApplicationGraphNode(
                    $"trm-component:{NormalizeHierarchyLabel(path.TrmComponentLabel, "TRM component")}",
                    NormalizeHierarchyLabel(path.TrmComponentLabel, "TRM component")),
                new ApplicationGraphNode(
                    path.ProductId.HasValue
                        ? $"product:{path.ProductId.Value}"
                        : $"product:{NormalizeHierarchyLabel(path.ProductLabel, "Product")}",
                    NormalizeHierarchyLabel(path.ProductLabel, "Product"))
            };

            for (var index = 0; index < nodes.Length - 1; index++)
            {
                var fromNode = nodes[index];
                var toNode = nodes[index + 1];
                if (fromNode.Id == toNode.Id)
                {
                    continue;
                }

                var edgeKey = $"{fromNode.Id}->{toNode.Id}";
                if (!seenEdges.Add(edgeKey))
                {
                    continue;
                }

                connections.Add(new ApplicationGraphConnectionViewModel
                {
                    FromId = fromNode.Id,
                    ToId = toNode.Id,
                    FromName = fromNode.Label,
                    ToName = toNode.Label
                });
            }
        }

        return connections;
    }

    private static List<ApplicationHierarchyNodeViewModel> BuildApplicationHierarchyNodes(
        List<ApplicationResolvedPathViewModel> paths,
        int level,
        string keyPrefix)
    {
        if (paths.Count == 0)
        {
            return [];
        }

        if (level == 6)
        {
            return paths
                .GroupBy(path => new
                {
                    path.ProductId,
                    Label = NormalizeHierarchyLabel(path.ProductLabel, "Product")
                })
                .OrderBy(group => group.Key.Label, StringComparer.OrdinalIgnoreCase)
                .Select((group, index) => new ApplicationHierarchyNodeViewModel
                {
                    Key = $"{keyPrefix}-product-{index}",
                    NodeType = "Product",
                    CssType = "product",
                    Label = group.Key.Label,
                    ProductId = group.Key.ProductId,
                    PathCount = group.Count(),
                    ProductCount = 1,
                    IsExpanded = true
                })
                .ToList();
        }

        string nodeType;
        string cssType;
        string fallbackLabel;
        Func<ApplicationResolvedPathViewModel, string> labelSelector;

        switch (level)
        {
            case 0:
                nodeType = "ARM domain";
                cssType = "arm-domain";
                fallbackLabel = "ARM domain";
                labelSelector = static path => path.ArmDomainLabel;
                break;
            case 1:
                nodeType = "ARM capability";
                cssType = "arm-capability";
                fallbackLabel = "ARM capability";
                labelSelector = static path => path.ArmCapabilityLabel;
                break;
            case 2:
                nodeType = "ARM component";
                cssType = "arm-component";
                fallbackLabel = "ARM component";
                labelSelector = static path => path.ArmComponentLabel;
                break;
            case 3:
                nodeType = "TRM domain";
                cssType = "trm-domain";
                fallbackLabel = "TRM domain";
                labelSelector = static path => path.TrmDomainLabel;
                break;
            case 4:
                nodeType = "TRM capability";
                cssType = "trm-capability";
                fallbackLabel = "TRM capability";
                labelSelector = static path => path.TrmCapabilityLabel;
                break;
            default:
                nodeType = "TRM component";
                cssType = "trm-component";
                fallbackLabel = "TRM component";
                labelSelector = static path => path.TrmComponentLabel;
                break;
        }

        return paths
            .GroupBy(path => NormalizeHierarchyLabel(labelSelector(path), fallbackLabel))
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Select((group, index) =>
            {
                var childPaths = group.ToList();
                return new ApplicationHierarchyNodeViewModel
                {
                    Key = $"{keyPrefix}-{cssType}-{index}",
                    NodeType = nodeType,
                    CssType = cssType,
                    Label = group.Key,
                    PathCount = childPaths.Count,
                    ProductCount = CountDistinctProducts(childPaths),
                    IsExpanded = true,
                    Children = BuildApplicationHierarchyNodes(childPaths, level + 1, $"{keyPrefix}-{cssType}-{index}")
                };
            })
            .ToList();
    }

    private static List<ApplicationHierarchyNodeViewModel> BuildCapabilityHierarchyNodes(
        List<CapabilityResolvedPathViewModel> paths,
        int level,
        string keyPrefix)
    {
        if (paths.Count == 0)
        {
            return [];
        }

        string nodeType;
        string cssType;
        string fallbackLabel;
        Func<CapabilityResolvedPathViewModel, string> labelSelector;

        switch (level)
        {
            case 0:
                nodeType = "BRM domain";
                cssType = "brm-domain";
                fallbackLabel = "BRM domain";
                labelSelector = static path => path.BrmDomainLabel;
                break;
            case 1:
                nodeType = "BRM capability";
                cssType = "brm-capability";
                fallbackLabel = "BRM capability";
                labelSelector = static path => path.BrmCapabilityLabel;
                break;
            case 2:
                nodeType = "BRM component";
                cssType = "brm-component";
                fallbackLabel = "BRM component";
                labelSelector = static path => path.BrmComponentLabel;
                break;
            case 3:
                nodeType = "ARM domain";
                cssType = "arm-domain";
                fallbackLabel = "ARM domain";
                labelSelector = static path => path.ArmDomainLabel;
                break;
            case 4:
                nodeType = "ARM capability";
                cssType = "arm-capability";
                fallbackLabel = "ARM capability";
                labelSelector = static path => path.ArmCapabilityLabel;
                break;
            case 5:
                nodeType = "ARM component";
                cssType = "arm-component";
                fallbackLabel = "ARM component";
                labelSelector = static path => path.ArmComponentLabel;
                break;
            case 6:
                nodeType = "Application";
                cssType = "application";
                fallbackLabel = "Application";
                labelSelector = static path => path.ApplicationName;
                break;
            case 7:
                nodeType = "TRM domain";
                cssType = "trm-domain";
                fallbackLabel = "TRM domain";
                labelSelector = static path => path.TrmDomainLabel;
                break;
            case 8:
                nodeType = "TRM capability";
                cssType = "trm-capability";
                fallbackLabel = "TRM capability";
                labelSelector = static path => path.TrmCapabilityLabel;
                break;
            default:
                nodeType = "TRM component";
                cssType = "trm-component";
                fallbackLabel = "TRM component";
                labelSelector = static path => path.TrmComponentLabel;
                break;
        }

        return paths
            .GroupBy(path => NormalizeHierarchyLabel(labelSelector(path), fallbackLabel))
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Select((group, index) =>
            {
                var childPaths = group.ToList();
                return new ApplicationHierarchyNodeViewModel
                {
                    Key = $"{keyPrefix}-{cssType}-{index}",
                    NodeType = nodeType,
                    CssType = cssType,
                    Label = group.Key,
                    PathCount = childPaths.Count,
                    ProductCount = CountDistinctProducts(childPaths),
                    IsExpanded = true,
                    Children = BuildCapabilityChildren(childPaths, level, $"{keyPrefix}-{cssType}-{index}")
                };
            })
            .ToList();
    }

    private static IReadOnlyList<ApplicationHierarchyNodeViewModel> BuildCapabilityChildren(
        List<CapabilityResolvedPathViewModel> childPaths,
        int level,
        string keyPrefix)
    {
        if (level >= 9)
        {
            return [];
        }

        if (level == 5)
        {
            var applicationPaths = childPaths
                .Where(HasResolvedApplication)
                .ToList();

            return applicationPaths.Count == 0
                ? []
                : BuildCapabilityHierarchyNodes(applicationPaths, level + 1, keyPrefix);
        }

        return BuildCapabilityHierarchyNodes(childPaths, level + 1, keyPrefix);
    }

    private static string NormalizeHierarchyLabel(string? label, string fallbackLabel)
    {
        var trimmed = label?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) || trimmed == "-"
            ? $"Unresolved {fallbackLabel}"
            : trimmed;
    }

    private static bool HasResolvedApplication(CapabilityResolvedPathViewModel path) =>
        HasResolvedLabel(path.ApplicationName);

    private static bool HasResolvedProduct(ApplicationResolvedPathViewModel path) =>
        path.ProductId.HasValue || HasResolvedLabel(path.ProductLabel);

    private static bool HasResolvedProduct(CapabilityResolvedPathViewModel path) =>
        path.ProductId.HasValue || HasResolvedLabel(path.ProductLabel);

    private static bool HasResolvedLabel(string? label)
    {
        var trimmed = label?.Trim();
        return !string.IsNullOrWhiteSpace(trimmed) && trimmed != "-";
    }

    private static int CountDistinctProducts(IEnumerable<ApplicationResolvedPathViewModel> paths) =>
        paths.Where(HasResolvedProduct)
            .Select(path => path.ProductId.HasValue ? $"id:{path.ProductId.Value}" : $"label:{path.ProductLabel}")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

    private static int CountDistinctProducts(IEnumerable<CapabilityResolvedPathViewModel> paths) =>
        paths.Where(HasResolvedProduct)
            .Select(path => path.ProductId.HasValue
                ? $"id:{path.ProductId.Value}"
                : $"label:{NormalizeHierarchyLabel(path.ProductLabel, "Product")}")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

    private readonly record struct ApplicationGraphNode(string Id, string Label);

    private static string BuildProductLabel(ProductCatalogItem? product)
    {
        if (product is null)
        {
            return "-";
        }

        if (string.IsNullOrWhiteSpace(product.Vendor))
        {
            return product.Name;
        }

        return $"{product.Name} ({product.Vendor})";
    }

    private static string BuildArmDomainLabel(ArmComponent? component) =>
        component?.ParentCapability?.ParentDomain is null
            ? "-"
            : $"{component.ParentCapability.ParentDomain.Code} {component.ParentCapability.ParentDomain.Name}";

    private static string BuildArmDomainLabel(ArmCapability? capability) =>
        capability?.ParentDomain is null
            ? "-"
            : $"{capability.ParentDomain.Code} {capability.ParentDomain.Name}";

    private static string BuildArmCapabilityLabel(ArmComponent? component) =>
        component?.ParentCapability is null
            ? "-"
            : $"{component.ParentCapability.Code} {component.ParentCapability.Name}";

    private static string BuildArmCapabilityLabel(ArmCapability? capability) =>
        capability is null
            ? "-"
            : $"{capability.Code} {capability.Name}";

    private static ArmCapability? GetResolvedArmCapability(BusinessCapabilityCatalogItemMapping mapping) =>
        mapping.ArmCapability ?? mapping.ArmComponent?.ParentCapability;

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
