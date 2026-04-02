using System.ComponentModel.DataAnnotations;
using HERMMapperApp.Models;

namespace HERMMapperApp.ViewModels;

public sealed class AiMappingSectionViewModel
{
    public AiMappingConfigurationInputModel Input { get; init; } = new();
    public bool IsEnabled { get; init; }
    public bool IsConfigured { get; init; }
    public bool HasSavedApiKey { get; init; }
    public string SavedApiKeyDisplay { get; init; } = "Not stored";
    public string StatusSummary { get; init; } = "Not configured";
    public bool CanLookup => IsEnabled && IsConfigured;
}

public sealed class AiMappingConfigurationInputModel
{
    [Required, StringLength(2048)]
    [Display(Name = "Chat completion endpoint")]
    public string Endpoint { get; set; } = string.Empty;

    [Required, StringLength(200)]
    [Display(Name = "Model")]
    public string Model { get; set; } = string.Empty;

    [StringLength(4000)]
    [DataType(DataType.Password)]
    [Display(Name = "API key")]
    public string? ApiKey { get; set; }

    [Range(1, 600)]
    [Display(Name = "Lookup timeout (seconds)")]
    public int TimeoutSeconds { get; set; } = AppSettingDefaults.AiMappingTimeoutSeconds;
}

public sealed class AiMappingReviewViewModel
{
    [Required]
    public int? ProductId { get; set; }

    public string ProductName { get; set; } = string.Empty;
    public string? Vendor { get; set; }
    public string? Version { get; set; }
    public string? LookupSummary { get; set; }
    public string? LookupError { get; set; }
    public int TimeoutSeconds { get; set; } = AppSettingDefaults.AiMappingTimeoutSeconds;
    public bool? AutoStartLookup { get; set; }
    public bool? LookupCompleted { get; set; }
    public List<AiMappingSuggestionSelectionViewModel> Suggestions { get; set; } = [];
}

public sealed class AiMappingSuggestionSelectionViewModel
{
    public int ComponentId { get; set; }
    public string DomainLabel { get; set; } = string.Empty;
    public string CapabilityLabel { get; set; } = string.Empty;
    public string ComponentLabel { get; set; } = string.Empty;
    public decimal Confidence { get; set; }
    public string ConfidenceLabel { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public bool Selected { get; set; } = true;
}
