namespace HERM_MAPPER_APP.ViewModels;

public sealed class OwnerVisualizationViewModel
{
    public int OwnerCount { get; init; }
    public int DomainCount { get; init; }
    public int CapabilityCount { get; init; }
    public int ComponentCount { get; init; }
    public int ProductCount { get; init; }
    public int MappingPathCount { get; init; }
    public string? SelectedLifecycleOwner { get; init; }
    public int LifecycleProductCount { get; init; }
    public IReadOnlyList<string> AvailableOwners { get; init; } = [];
    public IReadOnlyList<LifecycleStatusReportRowViewModel> LifecycleStatuses { get; init; } = [];
    public IReadOnlyList<OwnerHierarchyNodeViewModel> Owners { get; init; } = [];
    public IReadOnlyList<OwnerPathViewModel> Paths { get; init; } = [];
    public IReadOnlyList<OwnerSankeyNodeViewModel> SankeyNodes { get; init; } = [];
    public IReadOnlyList<OwnerSankeyLinkViewModel> SankeyLinks { get; init; } = [];
}

public sealed class LifecycleStatusReportRowViewModel
{
    public string Label { get; init; } = string.Empty;
    public int ProductCount { get; init; }
    public decimal Percentage { get; init; }
    public IReadOnlyList<LifecycleStatusProductViewModel> Products { get; init; } = [];
}

public sealed class LifecycleStatusProductViewModel
{
    public int ProductId { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Vendor { get; init; }
    public string? Version { get; init; }
    public string? OwnersLabel { get; init; }
}

public sealed class OwnerPathViewModel
{
    public int MappingId { get; init; }
    public string OwnerName { get; init; } = string.Empty;
    public int DomainId { get; init; }
    public string DomainLabel { get; init; } = string.Empty;
    public int CapabilityId { get; init; }
    public string CapabilityLabel { get; init; } = string.Empty;
    public int ComponentId { get; init; }
    public string ComponentLabel { get; init; } = string.Empty;
    public int ProductId { get; init; }
    public string ProductName { get; init; } = string.Empty;
}

public sealed class OwnerHierarchyNodeViewModel
{
    public string Key { get; init; } = string.Empty;
    public string NodeType { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;
    public int MappingCount { get; init; }
    public int ProductCount { get; init; }
    public int? ProductId { get; init; }
    public bool IsExpanded { get; init; }
    public IReadOnlyList<OwnerHierarchyNodeViewModel> Children { get; init; } = [];
}

public sealed class OwnerSankeyNodeViewModel
{
    public string Id { get; init; } = string.Empty;
    public string NodeType { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;
    public int Depth { get; init; }
    public int Value { get; init; }
}

public sealed class OwnerSankeyLinkViewModel
{
    public string SourceId { get; init; } = string.Empty;
    public string TargetId { get; init; } = string.Empty;
    public int Value { get; init; }
    public string LinkType { get; init; } = string.Empty;
}
