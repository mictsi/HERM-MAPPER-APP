namespace HERMMapperApp.ViewModels;

public sealed class TrmWorkbookImportSummary
{
    public Models.ReferenceModelKind ModelKind { get; init; } = Models.ReferenceModelKind.Trm;
    public int DomainsAdded { get; init; }
    public int DomainsUpdated { get; init; }
    public int CapabilitiesAdded { get; init; }
    public int CapabilitiesUpdated { get; init; }
    public int ComponentsAdded { get; init; }
    public int ComponentsUpdated { get; init; }
    public string ModelDisplayName => Models.ReferenceModelCatalog.GetDisplayName(ModelKind);
    public string DomainLabel => Models.ReferenceModelCatalog.GetDomainLabel(ModelKind);
    public string CapabilityLabel => Models.ReferenceModelCatalog.GetCapabilityLabel(ModelKind);
    public string ComponentLabel => Models.ReferenceModelCatalog.GetComponentLabel(ModelKind);
    public IReadOnlyList<WorkbookImportLayerSummary> LayerSummaries { get; init; } = [];
}
