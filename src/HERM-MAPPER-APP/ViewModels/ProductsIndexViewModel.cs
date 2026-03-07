using HERM_MAPPER_APP.Models;

namespace HERM_MAPPER_APP.ViewModels;

public sealed class ProductsIndexViewModel
{
    public string? Search { get; init; }
    public string? ImportStatusMessage { get; init; }
    public IReadOnlyList<ProductCatalogItem> Products { get; init; } = [];
}
