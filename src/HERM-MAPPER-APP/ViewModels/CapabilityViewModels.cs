using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace HERMMapperApp.ViewModels;

public sealed class CapabilitiesIndexViewModel
{
    public string? Search { get; init; }

    [Display(Name = "BRM model")]
    public int? BrmModelId { get; init; }

    public IReadOnlyList<SelectListItem> BrmModelOptions { get; init; } = [];
    public bool HasBrmModels { get; init; }

    public IReadOnlyList<CapabilityIndexRowViewModel> Capabilities { get; init; } = [];
}

public sealed class CapabilityIndexRowViewModel
{
    public int Id { get; init; }
    public int? BrmModelId { get; init; }
    public string BrmModelName { get; init; } = "-";
    public string BrmModelArea { get; init; } = "-";
    public string BrmModelStatus { get; init; } = "-";
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
    public int? Id { get; set; }

    [Required(ErrorMessage = "Choose a BRM model.")]
    [Display(Name = "BRM model")]
    public int? SelectedBrmModelId { get; set; }

    public string BrmModelName { get; set; } = string.Empty;
    public string BrmModelArea { get; set; } = string.Empty;
    public string BrmModelStatus { get; set; } = string.Empty;

    [Required(ErrorMessage = "Choose a BRM capability.")]
    [Display(Name = "BRM capability")]
    public int? SelectedBrmComponentId { get; set; }

    [StringLength(2000)]
    public string? Description { get; set; }

    [StringLength(4000)]
    public string? Notes { get; set; }

    public List<CapabilityMappingRowInputViewModel> MappingRows { get; set; } = [];

    public IReadOnlyList<SelectListItem> BrmComponentOptions { get; set; } = [];

    public IReadOnlyList<SelectListItem> ArmComponentOptions { get; set; } = [];

    public IReadOnlyList<SelectListItem> ArmCapabilityOptions { get; set; } = [];

    public IReadOnlyList<CapabilityArmComponentOptionViewModel> ArmComponentLookupOptions { get; set; } = [];
}

public sealed class CapabilityMappingRowInputViewModel
{
    [Display(Name = "Supporting ARM component")]
    public int? ArmComponentId { get; set; }

    [Display(Name = "ARM capability connection")]
    public int? ArmCapabilityId { get; set; }
}

public sealed class CapabilityDetailsViewModel
{
    public int Id { get; init; }
    public int? BrmModelId { get; init; }
    public string BrmModelName { get; init; } = "-";
    public string BrmModelArea { get; init; } = "-";
    public string BrmModelStatus { get; init; } = "-";
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public string? Notes { get; init; }
    public DateTime UpdatedUtc { get; init; }

    public IReadOnlyList<CapabilityMappingRowViewModel> MappingRows { get; init; } = [];

    public IReadOnlyList<CapabilityResolvedPathViewModel> ResolvedPaths { get; init; } = [];

    public ApplicationHierarchyNodeViewModel HierarchyRoot { get; init; } = new();

    public int BrmCapabilityCount { get; init; }
    public int ArmComponentCount { get; init; }
    public int ApplicationCount { get; init; }
    public int ProductCount { get; init; }

    public bool HasResolvedPaths => ResolvedPaths.Count != 0;
}

public sealed class CapabilityArmComponentOptionViewModel
{
    public int ArmComponentId { get; init; }
    public string ArmComponentLabel { get; init; } = string.Empty;
    public IReadOnlyList<CapabilityArmCapabilityOptionViewModel> CapabilityOptions { get; init; } = [];
}

public sealed class CapabilityDeleteViewModel
{
    public int Id { get; init; }
    public int? BrmModelId { get; init; }
    public string BrmModelName { get; init; } = "-";
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public int ArmComponentCount { get; init; }
    public DateTime UpdatedUtc { get; init; }
}

public sealed class CapabilityArmCapabilityOptionViewModel
{
    public int ArmCapabilityId { get; init; }
    public string ArmDomainLabel { get; init; } = string.Empty;
    public string ArmCapabilityLabel { get; init; } = string.Empty;
    public string ConnectionLabel { get; init; } = string.Empty;
}

public sealed class CapabilityMappingRowViewModel
{
    public string BrmDomainLabel { get; init; } = "-";
    public string BrmCapabilityLabel { get; init; } = "-";
    public string BrmComponentLabel { get; init; } = "-";
    public string ArmDomainLabel { get; init; } = "-";
    public string ArmCapabilityLabel { get; init; } = "-";
    public string ArmComponentLabel { get; init; } = "-";
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
    public int? ProductId { get; init; }
    public string TrmDomainLabel { get; init; } = "-";
    public string TrmCapabilityLabel { get; init; } = "-";
    public string TrmComponentLabel { get; init; } = "-";
    public string MappingStatus { get; init; } = "-";
}
