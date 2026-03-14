using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace HERMMapperApp.ViewModels;

public sealed class ServiceConnectionEditorViewModel
{
    public int ServiceId { get; set; }
    public string ServiceName { get; set; } = string.Empty;
    public string? ServiceDescription { get; set; }
    public string ServiceOwner { get; set; } = string.Empty;
    public string ServiceLifecycleStatus { get; set; } = string.Empty;
    public string? StatusMessage { get; set; }
    public bool UsesLegacyFlow { get; set; }
    public List<ServiceConnectionRowInputViewModel> ConnectionRows { get; set; } = [];
    public IReadOnlyList<SelectListItem> ProductOptions { get; set; } = [];
}

public sealed class ServiceConnectionRowInputViewModel
{
    [Display(Name = "From product")]
    public int? FromProductId { get; set; }

    [Display(Name = "To product")]
    public int? ToProductId { get; set; }
}
