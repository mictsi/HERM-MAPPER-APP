using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace HERMMapperApp.ViewModels;

public sealed class ApplicationsIndexViewModel
{
    public string? Search { get; init; }

    public IReadOnlyList<ApplicationIndexRowViewModel> Applications { get; init; } = [];
}

public sealed class ApplicationIndexRowViewModel
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public int ArmComponentCount { get; init; }
    public int ProductCount { get; init; }
    public int ResolvedPathCount { get; init; }
    public DateTime UpdatedUtc { get; init; }
}

public sealed class ApplicationEditViewModel
{
    public int? Id { get; set; }

    [Required, StringLength(200)]
    [Display(Name = "Application name")]
    public string Name { get; set; } = string.Empty;

    [StringLength(2000)]
    public string? Description { get; set; }

    [StringLength(4000)]
    public string? Notes { get; set; }

    public List<ApplicationMappingRowInputViewModel> MappingRows { get; set; } = [];

    public IReadOnlyList<SelectListItem> ArmComponentOptions { get; set; } = [];

    public IReadOnlyList<SelectListItem> ProductOptions { get; set; } = [];

    public IReadOnlyList<SelectListItem> TrmComponentOptions { get; set; } = [];

    public IReadOnlyList<ApplicationProductTrmMappingOptionViewModel> ProductTrmMappingOptions { get; set; } = [];
}

public sealed class ApplicationMappingRowInputViewModel
{
    [Display(Name = "ARM component")]
    public int? ArmComponentId { get; set; }

    [Display(Name = "Product")]
    public int? ProductCatalogItemId { get; set; }

    [Display(Name = "TRM component")]
    public int? TrmComponentId { get; set; }
}

public sealed class ApplicationDetailsViewModel
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public string? Notes { get; init; }
    public DateTime UpdatedUtc { get; init; }

    public IReadOnlyList<ApplicationMappingRowViewModel> MappingRows { get; init; } = [];

    public IReadOnlyList<ApplicationResolvedPathViewModel> ResolvedPaths { get; init; } = [];

    public ApplicationHierarchyNodeViewModel HierarchyRoot { get; init; } = new();

    public IReadOnlyList<ApplicationGraphConnectionViewModel> GraphConnections { get; init; } = [];

    public int ArmComponentCount { get; init; }
    public int ProductCount { get; init; }

    public bool HasResolvedPaths => ResolvedPaths.Count != 0;
}

public sealed class ApplicationMappingRowViewModel
{
    public string ArmDomainLabel { get; init; } = "-";
    public string ArmCapabilityLabel { get; init; } = "-";
    public string ArmComponentLabel { get; init; } = "-";
    public string ProductLabel { get; init; } = "-";
    public string TrmDomainLabel { get; init; } = "-";
    public string TrmCapabilityLabel { get; init; } = "-";
    public string TrmComponentLabel { get; init; } = "-";
    public string MappingStatus { get; init; } = "-";
}

public sealed class ApplicationResolvedPathViewModel
{
    public string ArmDomainLabel { get; init; } = "-";
    public string ArmCapabilityLabel { get; init; } = "-";
    public string ArmComponentLabel { get; init; } = "-";
    public string ProductLabel { get; init; } = "-";
    public int? ProductId { get; init; }
    public string TrmDomainLabel { get; init; } = "-";
    public string TrmCapabilityLabel { get; init; } = "-";
    public string TrmComponentLabel { get; init; } = "-";
    public string MappingStatus { get; init; } = "-";
}

public sealed class ApplicationHierarchyNodeViewModel
{
    public string Key { get; init; } = string.Empty;
    public string NodeType { get; init; } = string.Empty;
    public string CssType { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;
    public int PathCount { get; init; }
    public int ProductCount { get; init; }
    public int? ProductId { get; init; }
    public bool IsExpanded { get; init; }
    public IReadOnlyList<ApplicationHierarchyNodeViewModel> Children { get; init; } = [];
}

public sealed class ApplicationGraphConnectionViewModel
{
    public string FromId { get; init; } = string.Empty;
    public string ToId { get; init; } = string.Empty;
    public string FromName { get; init; } = string.Empty;
    public string ToName { get; init; } = string.Empty;
}

public sealed class ApplicationProductTrmMappingOptionViewModel
{
    public int ProductCatalogItemId { get; init; }
    public string ProductLabel { get; init; } = string.Empty;
    public int TrmComponentId { get; init; }
    public string TrmComponentLabel { get; init; } = string.Empty;
    public int ProductMappingId { get; init; }
}

public sealed class HierarchyDiagramPageViewModel
{
    public string Title { get; init; } = string.Empty;
    public string Eyebrow { get; init; } = string.Empty;
    public string Heading { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string BackLabel { get; init; } = string.Empty;
    public string BackAction { get; init; } = string.Empty;
    public ApplicationHierarchyNodeViewModel HierarchyRoot { get; init; } = new();
    public string EmptyTitle { get; init; } = "No dependency map yet";
    public string EmptyBody { get; init; } = "Add mappings to generate the dependency diagram.";
    public string Note { get; init; } = "Drag to pan and use the mouse wheel to zoom. The tree reads from left to right.";
    public bool IncludeProducts { get; init; }
}
