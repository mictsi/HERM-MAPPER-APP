using HERMMapperApp.Models;

namespace HERMMapperApp.ViewModels;

public sealed class ReferenceRestoreViewModel
{
    public ReferenceModelKind ModelKind { get; init; }
    public string PageTitle { get; init; } = string.Empty;
    public string Eyebrow { get; init; } = string.Empty;
    public string Heading { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string EmptyHeading { get; init; } = string.Empty;
    public string EmptyDescription { get; init; } = string.Empty;
    public string AdminNavKey { get; init; } = string.Empty;
    public IReadOnlyList<ReferenceRestoreItemViewModel> Components { get; init; } = [];
    public string? StatusMessage { get; init; }
}

public sealed class ReferenceRestoreItemViewModel
{
    public ReferenceModelKind ModelKind { get; init; }
    public int Id { get; init; }
    public string DisplayLabel { get; init; } = string.Empty;
    public string CapabilitiesText { get; init; } = "-";
    public DateTime? DeletedUtc { get; init; }
    public string? DeletedReason { get; init; }
    public bool SupportsHistory { get; init; }
}
