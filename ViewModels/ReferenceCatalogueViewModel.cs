using HERM_MAPPER_APP.Models;

namespace HERM_MAPPER_APP.ViewModels;

public sealed class ReferenceCatalogueViewModel
{
    public string? Search { get; init; }
    public int? DomainId { get; init; }
    public int? CapabilityId { get; init; }
    public IReadOnlyList<TrmDomain> Domains { get; init; } = [];
    public IReadOnlyList<TrmCapability> Capabilities { get; init; } = [];
    public IReadOnlyList<TrmComponent> Components { get; init; } = [];
    public WorkbookImportReviewViewModel ImportReview { get; init; } = new();
    public string? ImportStatusMessage { get; init; }
}
