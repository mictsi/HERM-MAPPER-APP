using System.ComponentModel.DataAnnotations;
using HERMMapperApp.Models;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace HERMMapperApp.ViewModels;

public sealed class ProductEditViewModel
{
    public int Id { get; set; }

    [Required, StringLength(200)]
    [Display(Name = "Product name")]
    public string Name { get; set; } = string.Empty;

    [StringLength(120)]
    public string? Vendor { get; set; }

    [StringLength(80)]
    public string? Version { get; set; }

    [StringLength(80)]
    [Display(Name = "Lifecycle status")]
    public string? LifecycleStatus { get; set; }

    [Display(Name = "Owners")]
    public List<string> Owners { get; set; } = [];

    [StringLength(2000)]
    public string? Description { get; set; }

    [StringLength(4000)]
    public string? Notes { get; set; }

    public IReadOnlyList<SelectListItem> OwnerOptions { get; set; } = [];
    public IReadOnlyList<SelectListItem> LifecycleStatusOptions { get; set; } = [];

    public static ProductEditViewModel FromProduct(ProductCatalogItem product) =>
        new()
        {
            Id = product.Id,
            Name = product.Name,
            Vendor = product.Vendor,
            Version = product.Version,
            LifecycleStatus = product.LifecycleStatus,
            Owners = product.GetOwnerValues().ToList(),
            Description = product.Description,
            Notes = product.Notes
        };
}
