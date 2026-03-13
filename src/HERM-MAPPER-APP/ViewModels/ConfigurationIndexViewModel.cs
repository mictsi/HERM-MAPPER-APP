using System.ComponentModel.DataAnnotations;
using HERMMapperApp.Models;
using HERMMapperApp.Services;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace HERMMapperApp.ViewModels;

public sealed class ConfigurationIndexViewModel
{
    public string? StatusMessage { get; init; }
    public string? ErrorMessage { get; init; }
    public string DisplayTimeZoneId { get; init; } = AppSettingDefaults.DisplayTimeZone;
    public IReadOnlyList<SelectListItem> AvailableTimeZones { get; init; } = [];
    public WorkbookImportReviewViewModel CatalogueImportReview { get; init; } = new();
    public ProductImportReviewViewModel ProductImportReview { get; init; } = new();
    public IReadOnlyList<ConfigurationFieldGroupViewModel> Fields { get; init; } = [];
}

public sealed class ConfigurationFieldGroupViewModel
{
    public string FieldName { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;
    public IReadOnlyList<ConfigurableFieldOption> Options { get; init; } = [];
}

public sealed class AddConfigurationOptionInputModel
{
    [Required]
    public string FieldName { get; set; } = string.Empty;

    [Required, StringLength(120)]
    [Display(Name = "Value")]
    public string Value { get; set; } = string.Empty;
}

public sealed class UpdateConfigurationOptionOrderInputModel
{
    [Required]
    public int Id { get; set; }

    [Range(1, int.MaxValue)]
    [Display(Name = "Order")]
    public int SortOrder { get; set; }
}

public sealed class UpdateDisplayTimeZoneInputModel
{
    [Required]
    [Display(Name = "Time zone")]
    public string TimeZoneId { get; set; } = AppSettingDefaults.DisplayTimeZone;
}

public sealed class ProductImportReviewViewModel
{
    public bool HasReview => Verification is not null;
    public string? PendingImportToken { get; init; }
    public string? UploadedFileName { get; init; }
    public ProductRelationshipVerificationResult? Verification { get; init; }
}
