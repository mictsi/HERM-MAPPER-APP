using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace HERMMapperApp.ViewModels;

public sealed class CapabilitiesIndexViewModel
{
    public string? Search { get; init; }

    public IReadOnlyList<CapabilityIndexRowViewModel> Capabilities { get; init; } = [];
}

public sealed class CapabilityIndexRowViewModel
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public int BrmCapabilityCount { get; init; }
    public int ArmComponentCount { get; init; }
    public int ApplicationCount { get; init; }
    public int ProductCount { get; init; }
    public DateTime UpdatedUtc { get; init; }
}

public sealed class CapabilityEditViewModel
{
    public int Id { get; set; }

    [Required, StringLength(200)]
    [Display(Name = "Capability name")]
    public string Name { get; set; } = string.Empty;

    [StringLength(2000)]
    public string? Description { get; set; }

    [StringLength(4000)]
    public string? Notes { get; set; }

    public List<CapabilityMappingRowInputViewModel> MappingRows { get; set; } = [];

    public IReadOnlyList<SelectListItem> BrmComponentOptions { get; set; } = [];

    public IReadOnlyList<SelectListItem> ArmComponentOptions { get; set; } = [];
}

public sealed class CapabilityMappingRowInputViewModel
{
    [Display(Name = "BRM capability")]
    public int? BrmComponentId { get; set; }

    [Display(Name = "Supporting ARM component")]
    public int? ArmComponentId { get; set; }

    [Display(Name = "Primary mapping")]
    public bool IsPrimary { get; set; }

    [StringLength(1000)]
    public string? Notes { get; set; }
}

public sealed class CapabilityDetailsViewModel
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public string? Notes { get; init; }
    public DateTime UpdatedUtc { get; init; }

    public IReadOnlyList<CapabilityMappingRowViewModel> MappingRows { get; init; } = [];

    public IReadOnlyList<CapabilityResolvedPathViewModel> ResolvedPaths { get; init; } = [];

    public int BrmCapabilityCount { get; init; }
    public int ArmComponentCount { get; init; }
    public int ApplicationCount { get; init; }
    public int ProductCount { get; init; }

    public bool HasResolvedPaths => ResolvedPaths.Count != 0;
}

public sealed class CapabilityMappingRowViewModel
{
    public string BrmDomainLabel { get; init; } = "-";
    public string BrmCapabilityLabel { get; init; } = "-";
    public string BrmComponentLabel { get; init; } = "-";
    public string ArmDomainLabel { get; init; } = "-";
    public string ArmCapabilityLabel { get; init; } = "-";
    public string ArmComponentLabel { get; init; } = "-";
    public bool IsPrimary { get; init; }
    public string? Notes { get; init; }
    public int LinkedApplicationCount { get; init; }
}

public sealed class CapabilityResolvedPathViewModel
{
    public string BrmDomainLabel { get; init; } = "-";
    public string BrmCapabilityLabel { get; init; } = "-";
    public string BrmComponentLabel { get; init; } = "-";
    public string ArmDomainLabel { get; init; } = "-";
    public string ArmCapabilityLabel { get; init; } = "-";
    public string ArmComponentLabel { get; init; } = "-";
    public string ApplicationName { get; init; } = "-";
    public string ProductLabel { get; init; } = "-";
    public string TrmDomainLabel { get; init; } = "-";
    public string TrmCapabilityLabel { get; init; } = "-";
    public string TrmComponentLabel { get; init; } = "-";
    public string MappingStatus { get; init; } = "-";
}
