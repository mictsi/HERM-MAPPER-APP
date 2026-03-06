using System.ComponentModel.DataAnnotations;
using HERM_MAPPER_APP.Models;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace HERM_MAPPER_APP.ViewModels;

public sealed class MappingEditViewModel
{
    public int? MappingId { get; init; }
    public int ProductId { get; init; }
    public string ProductName { get; init; } = string.Empty;
    public string? Vendor { get; init; }
    public string? Version { get; init; }
    public string? Description { get; init; }
    public string? LifecycleStatus { get; init; }
    public string? Owner { get; init; }

    [Display(Name = "TRM domain")]
    public int? SelectedDomainId { get; set; }

    [Display(Name = "TRM capability")]
    public int? SelectedCapabilityId { get; set; }

    [Display(Name = "TRM component")]
    public int? SelectedComponentId { get; set; }

    [Display(Name = "Mapping status")]
    public MappingStatus MappingStatus { get; set; }

    [Range(1, 5)]
    [Display(Name = "Fit score")]
    public int? FitScore { get; set; }

    [StringLength(4000)]
    [Display(Name = "Mapping rationale")]
    public string? MappingRationale { get; set; }

    public IEnumerable<SelectListItem> Domains { get; init; } = [];
    public IEnumerable<SelectListItem> Capabilities { get; init; } = [];
    public IEnumerable<SelectListItem> Components { get; init; } = [];
}
