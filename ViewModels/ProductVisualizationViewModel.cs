using HERM_MAPPER_APP.Models;

namespace HERM_MAPPER_APP.ViewModels;

public sealed class ProductVisualizationViewModel
{
    public ProductCatalogItem Product { get; init; } = new();
    public IReadOnlyList<ProductDependencyPathViewModel> Paths { get; init; } = [];
}

public sealed class ProductDependencyPathViewModel
{
    public string Status { get; init; } = string.Empty;
    public string DomainLabel { get; init; } = "-";
    public string CapabilityLabel { get; init; } = "-";
    public string ComponentLabel { get; init; } = "-";
}
