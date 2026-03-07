using HERM_MAPPER_APP.Models;

namespace HERM_MAPPER_APP.ViewModels;

public sealed class ProductsIndexViewModel
{
    public string? Search { get; init; }
    public string? Owner { get; init; }
    public string? LifecycleStatus { get; init; }
    public IReadOnlyList<ConfigurableFieldOption> Owners { get; init; } = [];
    public IReadOnlyList<ConfigurableFieldOption> LifecycleStatuses { get; init; } = [];
    public IReadOnlyList<ProductCatalogItem> Products { get; init; } = [];
}
