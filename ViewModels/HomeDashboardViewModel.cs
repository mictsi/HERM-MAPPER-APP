using HERM_MAPPER_APP.Models;

namespace HERM_MAPPER_APP.ViewModels;

public sealed class HomeDashboardViewModel
{
    public int ProductCount { get; init; }
    public int CompletedMappings { get; init; }
    public int ReferenceComponentCount { get; init; }
    public int DomainCount { get; init; }
    public int CapabilityCount { get; init; }
    public string? WorkbookPath { get; init; }
    public IReadOnlyList<ProductCatalogItem> RecentProducts { get; init; } = [];
}
