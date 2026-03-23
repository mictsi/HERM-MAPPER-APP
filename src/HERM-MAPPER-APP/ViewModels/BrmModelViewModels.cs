using System.ComponentModel.DataAnnotations;

namespace HERMMapperApp.ViewModels;

public sealed class BrmModelsIndexViewModel
{
    public string? StatusMessage { get; init; }
    public IReadOnlyList<BrmModelIndexRowViewModel> Models { get; init; } = [];
}

public sealed class BrmModelIndexRowViewModel
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Area { get; init; } = string.Empty;
    public string? Description { get; init; }
    public string Status { get; init; } = string.Empty;
    public int CapabilityCount { get; init; }
    public DateTime UpdatedUtc { get; init; }
}

public sealed class BrmModelEditViewModel
{
    public int? Id { get; set; }

    [Required, StringLength(200)]
    public string Name { get; set; } = string.Empty;

    [Required, StringLength(120)]
    public string Area { get; set; } = string.Empty;

    [StringLength(2000)]
    public string? Description { get; set; }

    [Required, StringLength(80)]
    public string Status { get; set; } = string.Empty;

    public IReadOnlyList<string> SuggestedStatuses { get; init; } = [];
}

public sealed class BrmModelDetailsViewModel
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Area { get; init; } = string.Empty;
    public string? Description { get; init; }
    public string Status { get; init; } = string.Empty;
    public DateTime UpdatedUtc { get; init; }
    public int CapabilityCount { get; init; }
    public string? StatusMessage { get; init; }
    public ApplicationHierarchyNodeViewModel HierarchyRoot { get; init; } = new();
    public IReadOnlyList<BrmModelCapabilityRowViewModel> Capabilities { get; init; } = [];

    public bool HasDependencyTree => HierarchyRoot.Children.Count != 0;
}

public sealed class BrmModelCapabilityRowViewModel
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public int ArmComponentCount { get; init; }
    public int ApplicationCount { get; init; }
    public int ProductCount { get; init; }
    public DateTime UpdatedUtc { get; init; }
}
