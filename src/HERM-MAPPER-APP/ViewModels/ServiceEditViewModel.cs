using Microsoft.AspNetCore.Mvc.Rendering;
using System.ComponentModel.DataAnnotations;
using HERMMapperApp.Models;

namespace HERMMapperApp.ViewModels;

public sealed class ServiceEditViewModel
{
    public int Id { get; set; }

    [Required, StringLength(200)]
    [Display(Name = "Service name")]
    public string Name { get; set; } = string.Empty;

    [StringLength(2000)]
    public string? Description { get; set; }

    [Required, StringLength(120)]
    public string? Owner { get; set; }

    [Required, StringLength(80)]
    [Display(Name = "Lifecycle status")]
    public string? LifecycleStatus { get; set; }

    public IReadOnlyList<SelectListItem> OwnerOptions { get; set; } = [];
    public IReadOnlyList<SelectListItem> LifecycleStatusOptions { get; set; } = [];

    public static ServiceEditViewModel FromService(ServiceCatalogItem service) =>
        new()
        {
            Id = service.Id,
            Name = service.Name,
            Description = service.Description,
            Owner = service.Owner,
            LifecycleStatus = service.LifecycleStatus
        };
}
