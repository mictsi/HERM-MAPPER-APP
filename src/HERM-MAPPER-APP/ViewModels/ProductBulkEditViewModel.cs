using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace HERMMapperApp.ViewModels;

public sealed class ProductBulkEditViewModel : IValidatableObject
{
    public List<int> SelectedProductIds { get; set; } = [];
    public IReadOnlyList<ProductBulkEditSelectionViewModel> SelectedProducts { get; set; } = [];

    public string? ReturnSearch { get; set; }
    public List<string> ReturnOwners { get; set; } = [];
    public string? ReturnLifecycleStatus { get; set; }

    [Display(Name = "Update vendor")]
    public bool ApplyVendor { get; set; }

    [StringLength(120)]
    public string? Vendor { get; set; }

    [Display(Name = "Update owners")]
    public bool ApplyOwners { get; set; }

    [Display(Name = "Owner update mode")]
    public string OwnerUpdateMode { get; set; } = ProductBulkOwnerUpdateModes.Replace;

    [Display(Name = "Owners")]
    public List<string> Owners { get; set; } = [];

    [Display(Name = "Update lifecycle status")]
    public bool ApplyLifecycleStatus { get; set; }

    [StringLength(80)]
    [Display(Name = "Lifecycle status")]
    public string? LifecycleStatus { get; set; }

    public IReadOnlyList<SelectListItem> OwnerOptions { get; set; } = [];
    public IReadOnlyList<SelectListItem> LifecycleStatusOptions { get; set; } = [];

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (SelectedProductIds.Count == 0)
        {
            yield return new ValidationResult(
                "Select at least one product before bulk editing.",
                [nameof(SelectedProductIds)]);
        }

        if (!ApplyVendor && !ApplyOwners && !ApplyLifecycleStatus)
        {
            yield return new ValidationResult(
                "Choose at least one field to update.",
                [nameof(ApplyVendor), nameof(ApplyOwners), nameof(ApplyLifecycleStatus)]);
        }
    }
}

public static class ProductBulkOwnerUpdateModes
{
    public const string Replace = "replace";
    public const string Append = "append";
}

public sealed class ProductBulkEditSelectionViewModel
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Vendor { get; init; }
    public string? LifecycleStatus { get; init; }
    public IReadOnlyList<string> Owners { get; init; } = [];

    public string OwnerDisplay => Owners.Count == 0 ? "-" : string.Join(", ", Owners);
}
