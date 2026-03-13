using System.ComponentModel.DataAnnotations;
using HERMMapperApp.Models;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace HERMMapperApp.ViewModels;

public sealed class ServiceEditViewModel : IValidatableObject
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

    public List<ServiceProductRowViewModel> ProductRows { get; set; } = [];

    public IReadOnlyList<SelectListItem> OwnerOptions { get; set; } = [];
    public IReadOnlyList<SelectListItem> LifecycleStatusOptions { get; set; } = [];
    public IReadOnlyList<SelectListItem> ProductOptions { get; set; } = [];

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        var selectedProductIds = ProductRows
            .Where(x => x.ProductId is > 0)
            .Select(x => x.ProductId!.Value)
            .ToList();

        if (selectedProductIds.Count < 2)
        {
            yield return new ValidationResult(
                "Select at least two products to define a service connection.",
                [nameof(ProductRows)]);
        }

        if (selectedProductIds.Count != selectedProductIds.Distinct().Count())
        {
            yield return new ValidationResult(
                "Each product can only be selected once in a service.",
                [nameof(ProductRows)]);
        }
    }

    public static ServiceEditViewModel FromService(ServiceCatalogItem service) =>
        new()
        {
            Id = service.Id,
            Name = service.Name,
            Description = service.Description,
            Owner = service.Owner,
            LifecycleStatus = service.LifecycleStatus,
            ProductRows = service.GetOrderedProductLinks()
                .Select(x => new ServiceProductRowViewModel
                {
                    ProductId = x.ProductCatalogItemId
                })
                .ToList()
        };
}

public sealed class ServiceProductRowViewModel
{
    public int? ProductId { get; set; }
}
