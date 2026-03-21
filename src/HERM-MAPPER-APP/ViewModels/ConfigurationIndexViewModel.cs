using System.ComponentModel.DataAnnotations;
using HERMMapperApp.Models;
using HERMMapperApp.Services;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace HERMMapperApp.ViewModels;

public sealed class ConfigurationIndexViewModel
{
    public string? StatusMessage { get; init; }
    public string? ErrorMessage { get; init; }
    public string? ErrorSectionKey { get; init; }
    public string? ExpandedFieldName { get; init; }
    public bool OpenRemoteSqlImportSection { get; init; }
    public string DisplayTimeZoneId { get; init; } = AppSettingDefaults.DisplayTimeZone;
    public IReadOnlyList<SelectListItem> AvailableTimeZones { get; init; } = [];
    public ReferenceModelKind CatalogueImportModelKind { get; init; } = ReferenceModelKind.Trm;
    public IReadOnlyList<SelectListItem> CatalogueImportModelOptions { get; init; } = [];
    public WorkbookImportReviewViewModel CatalogueImportReview { get; init; } = new();
    public ProductImportReviewViewModel ProductImportReview { get; init; } = new();
    public RemoteSqlImportSectionViewModel RemoteSqlImport { get; init; } = new();
    public IReadOnlyList<ConfigurationFieldGroupViewModel> Fields { get; init; } = [];
}

public sealed class RemoteSqlImportSectionViewModel
{
    public RemoteSqlImportInputModel Input { get; init; } = new();
    public IReadOnlyList<SelectListItem> ScheduleOptions { get; init; } = [];
    public bool IsEnabled { get; init; } = AppSettingDefaults.RemoteSqlImportEnabled;
    public bool IsConfigured { get; init; }
    public bool HasSavedUserName { get; init; }
    public bool HasSavedPassword { get; init; }
    public string SavedUserNameDisplay { get; init; } = "Not stored";
    public string SavedPasswordDisplay { get; init; } = "Not stored";
    public string ScheduleSummary { get; init; } = "Manual only";
    public string StatusSummary { get; init; } = "Not configured";
    public string? LastMessage { get; init; }
    public DateTime? LastAttemptUtc { get; init; }
    public DateTime? LastSuccessUtc { get; init; }
    public DateTime? NextScheduledRunUtc { get; init; }
    public RemoteSqlImportConnectionTestViewModel? TestResult { get; init; }
    public string? SavedUserNameClearText { get; init; }
    public string? SavedPasswordClearText { get; init; }
    public bool CanRunImportNow => IsConfigured && IsEnabled;
}

public sealed class RemoteSqlImportInputModel
{
    [Required, StringLength(256)]
    [Display(Name = "Server")]
    public string ServerName { get; set; } = string.Empty;

    [Range(1, 65535)]
    [Display(Name = "Port")]
    public int Port { get; set; } = AppSettingDefaults.RemoteSqlImportPort;

    [Required, StringLength(256)]
    [Display(Name = "Database")]
    public string DatabaseName { get; set; } = string.Empty;

    [Display(Name = "Encrypt connection")]
    public bool Encrypt { get; set; } = AppSettingDefaults.RemoteSqlImportEncrypt;

    [Display(Name = "Trust server certificate")]
    public bool TrustServerCertificate { get; set; } = AppSettingDefaults.RemoteSqlImportTrustServerCertificate;

    [Display(Name = "Use integrated security")]
    public bool UseIntegratedSecurity { get; set; } = AppSettingDefaults.RemoteSqlImportUseIntegratedSecurity;

    [StringLength(256)]
    [Display(Name = "User name")]
    public string? UserName { get; set; }

    [StringLength(256)]
    [DataType(DataType.Password)]
    [Display(Name = "Password")]
    public string? Password { get; set; }

    [Display(Name = "Schedule")]
    public int ScheduleHours { get; set; } = AppSettingDefaults.RemoteSqlImportScheduleHours;
}

public sealed class RemoteSqlImportConnectionTestViewModel
{
    public bool IsSuccess { get; init; }
    public string Summary { get; init; } = string.Empty;
    public int RemoteProductCount { get; init; }
    public int RemoteMappingCount { get; init; }
    public bool OwnersTableAvailable { get; init; }
    public IReadOnlyList<string> Errors { get; init; } = [];
    public IReadOnlyList<string> Warnings { get; init; } = [];
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
    public int? Id { get; set; }

    [Required]
    [Range(1, int.MaxValue)]
    [Display(Name = "Order")]
    public int? SortOrder { get; set; }
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
