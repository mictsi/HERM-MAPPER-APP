using HERMMapperApp.Models;

namespace HERMMapperApp.ViewModels;

public sealed class ReferenceCatalogueViewModel
{
    public string? Search { get; init; }
    public int? DomainId { get; init; }
    public int? CapabilityId { get; init; }
    public ReferenceModelKind? SelectedModelKind { get; init; }
    public string? SelectedDomainCode { get; init; }
    public string? SelectedCapabilityCode { get; init; }
    public string? SelectedComponentCode { get; init; }
    public string? SelectedSubClassCode { get; init; }
    public string SelectionTitle { get; init; } = "All reference models";
    public string SelectionDescription { get; init; } = "Browse imported catalogue entries across TRM, ARM, BRM, and DRM.";
    public string ActiveTreeAnchorId { get; init; } = "browser-navigation";
    public IReadOnlyList<ReferenceBrowserModelViewModel> ModelGroups { get; init; } = [];
    public IReadOnlyList<ReferenceComponentBrowserItemViewModel> Components { get; init; } = [];
    public WorkbookImportReviewViewModel ImportReview { get; init; } = new();
    public string? ImportStatusMessage { get; init; }
}

public sealed class ReferenceBrowserModelViewModel
{
    public ReferenceModelKind ModelKind { get; init; }
    public string Label { get; init; } = string.Empty;
    public string ShortName { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string DomainLabel { get; init; } = string.Empty;
    public bool IsSelected { get; init; }
    public bool IsExpanded { get; init; }
    public IReadOnlyList<ReferenceBrowserDomainViewModel> Domains { get; init; } = [];
    public string AnchorId => $"browser-model-{ReferenceBrowserAnchorUtility.NormalizeAnchorSegment(ShortName)}";
}

public sealed class ReferenceBrowserDomainViewModel
{
    public ReferenceModelKind ModelKind { get; init; }
    public string Code { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public bool IsSelected { get; init; }
    public bool IsExpanded { get; init; }
    public IReadOnlyList<ReferenceBrowserCapabilityViewModel> Capabilities { get; init; } = [];

    public string DisplayLabel => string.IsNullOrWhiteSpace(Code)
        ? Name
        : $"{Code} {Name}";

    public string AnchorId =>
        $"browser-model-{ReferenceBrowserAnchorUtility.NormalizeAnchorSegment(ReferenceModelCatalog.GetShortName(ModelKind))}-domain-{ReferenceBrowserAnchorUtility.NormalizeAnchorSegment(Code)}";
}

public sealed class ReferenceBrowserCapabilityViewModel
{
    public ReferenceModelKind ModelKind { get; init; }
    public string ParentDomainCode { get; init; } = string.Empty;
    public string Code { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public bool IsSelected { get; init; }
    public IReadOnlyList<ReferenceBrowserComponentNodeViewModel> Components { get; init; } = [];

    public string DisplayLabel => string.IsNullOrWhiteSpace(Code)
        ? Name
        : $"{Code} {Name}";

    public string AnchorId =>
        $"browser-model-{ReferenceBrowserAnchorUtility.NormalizeAnchorSegment(ReferenceModelCatalog.GetShortName(ModelKind))}-domain-{ReferenceBrowserAnchorUtility.NormalizeAnchorSegment(ParentDomainCode)}-capability-{ReferenceBrowserAnchorUtility.NormalizeAnchorSegment(Code)}";
}

public sealed class ReferenceBrowserComponentNodeViewModel
{
    public ReferenceModelKind ModelKind { get; init; }
    public string ParentDomainCode { get; init; } = string.Empty;
    public string ParentCapabilityCode { get; init; } = string.Empty;
    public string? ParentComponentCode { get; init; }
    public string Code { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public bool IsSelected { get; init; }
    public IReadOnlyList<ReferenceBrowserComponentNodeViewModel> Children { get; init; } = [];

    public string DisplayLabel => string.IsNullOrWhiteSpace(Code)
        ? Name
        : $"{Code} {Name}";

    public string AnchorId
    {
        get
        {
            var modelAnchor = $"browser-model-{ReferenceBrowserAnchorUtility.NormalizeAnchorSegment(ReferenceModelCatalog.GetShortName(ModelKind))}";
            var capabilityAnchor =
                $"{modelAnchor}-domain-{ReferenceBrowserAnchorUtility.NormalizeAnchorSegment(ParentDomainCode)}-capability-{ReferenceBrowserAnchorUtility.NormalizeAnchorSegment(ParentCapabilityCode)}";

            if (string.IsNullOrWhiteSpace(ParentComponentCode))
            {
                return $"{capabilityAnchor}-component-{ReferenceBrowserAnchorUtility.NormalizeAnchorSegment(Code)}";
            }

            return $"{capabilityAnchor}-component-{ReferenceBrowserAnchorUtility.NormalizeAnchorSegment(ParentComponentCode)}-subclass-{ReferenceBrowserAnchorUtility.NormalizeAnchorSegment(Code)}";
        }
    }
}

public sealed class ReferenceComponentBrowserItemViewModel
{
    public ReferenceModelKind ModelKind { get; init; }
    public int NativeId { get; init; }
    public string ModelLabel { get; init; } = string.Empty;
    public string Code { get; init; } = string.Empty;
    public string? SecondaryCode { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public string? ProductExamples { get; init; }
    public string TypeLabel { get; init; } = string.Empty;
    public string? ParentComponentCode { get; init; }
    public bool IsCustom { get; init; }
    public bool SupportsHistory { get; init; }
    public bool SupportsDelete { get; init; }
    public IReadOnlyList<ReferenceBrowserLabelViewModel> Capabilities { get; init; } = [];
    public IReadOnlyList<ReferenceBrowserLabelViewModel> Domains { get; init; } = [];

    public string DisplayLabel => string.IsNullOrWhiteSpace(Code)
        ? Name
        : $"{Code} {Name}";
}

public sealed class ReferenceBrowserLabelViewModel
{
    public string Code { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;

    public string DisplayLabel => string.IsNullOrWhiteSpace(Code)
        ? Name
        : $"{Code} {Name}";
}

internal static class ReferenceBrowserAnchorUtility
{
    public static string NormalizeAnchorSegment(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? "item"
            : value.Trim().ToLowerInvariant().Replace(' ', '-');
}
