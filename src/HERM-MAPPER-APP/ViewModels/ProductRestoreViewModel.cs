using HERMMapperApp.Models;

namespace HERMMapperApp.ViewModels;

public sealed class ProductRestoreViewModel
{
    public IReadOnlyList<ProductCatalogItem> Products { get; init; } = [];
    public string? StatusMessage { get; init; }
}
