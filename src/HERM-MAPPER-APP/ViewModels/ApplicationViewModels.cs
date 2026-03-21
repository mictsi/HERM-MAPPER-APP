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
    public int Id { get; set; }

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
}

public sealed class ApplicationMappingRowInputViewModel
{
    [Display(Name = "ARM component")]
    public int? ArmComponentId { get; set; }

    [Display(Name = "Supporting TRM product")]
    public int? ProductCatalogItemId { get; set; }

    [Display(Name = "Primary mapping")]
    public bool IsPrimary { get; set; }

    [StringLength(1000)]
    public string? Notes { get; set; }
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
    public bool IsPrimary { get; init; }
    public string? Notes { get; init; }
    public int ResolvedTrmPathCount { get; init; }
}

public sealed class ApplicationResolvedPathViewModel
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
