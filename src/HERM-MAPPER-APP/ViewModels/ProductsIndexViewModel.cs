using HERM_MAPPER_APP.Models;

namespace HERM_MAPPER_APP.ViewModels;

public sealed class ProductsIndexViewModel
{
    public string? Search { get; init; }
    public IReadOnlyList<string> SelectedOwners { get; init; } = [];
    public string? LifecycleStatus { get; init; }
    public IReadOnlyList<ConfigurableFieldOption> AvailableOwners { get; init; } = [];
    public IReadOnlyList<ConfigurableFieldOption> LifecycleStatuses { get; init; } = [];
    public IReadOnlyList<ProductCatalogItem> Products { get; init; } = [];
}
