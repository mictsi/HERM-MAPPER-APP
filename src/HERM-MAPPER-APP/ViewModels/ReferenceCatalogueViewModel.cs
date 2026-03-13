using HERMMapperApp.Models;

namespace HERMMapperApp.ViewModels;

public sealed class ReferenceCatalogueViewModel
{
    public string? Search { get; init; }
    public int? DomainId { get; init; }
    public int? CapabilityId { get; init; }
    public IReadOnlyList<TrmDomain> Domains { get; init; } = [];
    public IReadOnlyList<TrmCapability> Capabilities { get; init; } = [];
    public IReadOnlyList<TrmComponent> Components { get; init; } = [];
    public IReadOnlyList<TrmComponent> TrashedComponents { get; init; } = [];
    public WorkbookImportReviewViewModel ImportReview { get; init; } = new();
    public string? ImportStatusMessage { get; init; }
}
