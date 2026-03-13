using HERMMapperApp.Models;

namespace HERMMapperApp.ViewModels;

public sealed class HomeDashboardViewModel
{
    public int ProductCount { get; init; }
    public int CompletedMappings { get; init; }
    public int ReferenceComponentCount { get; init; }
    public int DomainCount { get; init; }
    public int CapabilityCount { get; init; }
    public bool HasReferenceModel { get; init; }
    public IReadOnlyList<ProductCatalogItem> RecentProducts { get; init; } = [];
}
