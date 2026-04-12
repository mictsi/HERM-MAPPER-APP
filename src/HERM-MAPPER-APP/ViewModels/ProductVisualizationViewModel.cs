using HERMMapperApp.Models;

namespace HERMMapperApp.ViewModels;

public sealed class ProductVisualizationViewModel
{
    public ProductCatalogItem Product { get; init; } = new();
    public IReadOnlyList<ProductDependencyPathViewModel> Paths { get; init; } = [];
    public ApplicationHierarchyNodeViewModel HierarchyRoot { get; init; } = new();
    public string MappingReturnUrl { get; init; } = "/Products";
    public bool AiMappingLookupEnabled { get; set; }
    public bool AiMappingLookupConfigured { get; set; }

    public bool HasResolvedPaths => Paths.Count != 0;
    public bool CanUseAiMappingLookup => AiMappingLookupEnabled && AiMappingLookupConfigured;
}

public sealed class ProductDependencyPathViewModel
{
    public string Status { get; init; } = string.Empty;
    public string DomainLabel { get; init; } = "-";
    public string CapabilityLabel { get; init; } = "-";
    public string ComponentLabel { get; init; } = "-";
}
