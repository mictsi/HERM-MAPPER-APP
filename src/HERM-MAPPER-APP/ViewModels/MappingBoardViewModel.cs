using HERM_MAPPER_APP.Models;

namespace HERM_MAPPER_APP.ViewModels;

public sealed class MappingBoardViewModel
{
    public string? Search { get; init; }
    public MappingStatus? Status { get; init; }
    public int? DomainId { get; init; }
    public int? CapabilityId { get; init; }
    public int DraftCount { get; init; }
    public int InReviewCount { get; init; }
    public int CompleteCount { get; init; }
    public int OutOfScopeCount { get; init; }
    public IReadOnlyList<TrmDomain> Domains { get; init; } = [];
    public IReadOnlyList<TrmCapability> Capabilities { get; init; } = [];
    public IReadOnlyList<ProductCatalogItem> Products { get; init; } = [];
}
