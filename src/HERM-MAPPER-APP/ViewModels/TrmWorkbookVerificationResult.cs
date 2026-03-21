namespace HERMMapperApp.ViewModels;

public sealed class TrmWorkbookVerificationResult
{
    public Models.ReferenceModelKind ModelKind { get; init; } = Models.ReferenceModelKind.Trm;
    public bool IsValid => Errors.Count == 0;
    public int DomainRowCount { get; init; }
    public int CapabilityRowCount { get; init; }
    public int ComponentRowCount { get; init; }
    public int DomainsToAdd { get; init; }
    public int DomainsToUpdate { get; init; }
    public int CapabilitiesToAdd { get; init; }
    public int CapabilitiesToUpdate { get; init; }
    public int ComponentsToAdd { get; init; }
    public int ComponentsToUpdate { get; init; }
    public string ModelDisplayName => Models.ReferenceModelCatalog.GetDisplayName(ModelKind);
    public string DomainLabel => Models.ReferenceModelCatalog.GetDomainLabel(ModelKind);
    public string CapabilityLabel => Models.ReferenceModelCatalog.GetCapabilityLabel(ModelKind);
    public string ComponentLabel => Models.ReferenceModelCatalog.GetComponentLabel(ModelKind);
    public IReadOnlyList<WorkbookImportLayerSummary> LayerSummaries { get; init; } = [];
    public IReadOnlyList<string> Errors { get; init; } = [];
    public IReadOnlyList<string> Warnings { get; init; } = [];
}

public sealed class WorkbookImportLayerSummary
{
    public string Label { get; init; } = string.Empty;
    public int RowCount { get; init; }
    public int ToAdd { get; init; }
    public int ToUpdate { get; init; }
}
