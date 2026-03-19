using HERMMapperApp.Models;

namespace HERMMapperApp.ViewModels;

public sealed class ProductDependenciesViewModel
{
    public ProductCatalogItem Product { get; init; } = null!;
    public IReadOnlyList<ProductServiceDependencyViewModel> IncomingDependencies { get; init; } = [];
    public IReadOnlyList<ProductServiceDependencyViewModel> OutgoingDependencies { get; init; } = [];

    public bool HasDependencies => IncomingDependencies.Count != 0 || OutgoingDependencies.Count != 0;
}

public sealed class ProductServiceDependencyViewModel
{
    public int ServiceId { get; init; }
    public string ServiceName { get; init; } = string.Empty;
    public int RelatedProductId { get; init; }
    public string RelatedProductLabel { get; init; } = string.Empty;
    public bool CanOpenRelatedProduct { get; init; }
    public int Sequence { get; init; }
}
