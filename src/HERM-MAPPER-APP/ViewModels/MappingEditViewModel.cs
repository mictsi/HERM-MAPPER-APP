using System.ComponentModel.DataAnnotations;
using HERMMapperApp.Models;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace HERMMapperApp.ViewModels;

public sealed class MappingEditViewModel
{
    public int? MappingId { get; init; }
    public int ProductId { get; init; }
    public string ProductName { get; init; } = string.Empty;
    public string? Vendor { get; init; }
    public string? Version { get; init; }
    public string? Description { get; init; }
    public string? LifecycleStatus { get; init; }

    [Display(Name = "Owners")]
    public List<string> Owners { get; set; } = [];

    [Display(Name = "TRM domain")]
    public int? SelectedDomainId { get; set; }

    [Display(Name = "TRM capability")]
    public int? SelectedCapabilityId { get; set; }

    [Display(Name = "TRM component")]
    public int? SelectedComponentId { get; set; }

    [Display(Name = "Mapping status")]
    public MappingStatus MappingStatus { get; set; }

    [StringLength(32)]
    [Display(Name = "Technology Component Code")]
    public string? CustomTechnologyComponentCode { get; set; }

    [StringLength(200)]
    [Display(Name = "Custom component name")]
    public string? CustomComponentName { get; set; }

    [StringLength(4000)]
    [Display(Name = "Mapping rationale")]
    public string? MappingRationale { get; set; }

    public IEnumerable<SelectListItem> Domains { get; init; } = [];
    public IEnumerable<SelectListItem> Capabilities { get; init; } = [];
    public IEnumerable<SelectListItem> Components { get; init; } = [];
    public IEnumerable<SelectListItem> OwnerOptions { get; init; } = [];

    public string? OwnerSummary => Owners.Count == 0 ? null : string.Join(", ", Owners);
}
