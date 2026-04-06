using System.ComponentModel.DataAnnotations;
using System.Collections.Generic;
using HERMMapperApp.Models;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace HERMMapperApp.ViewModels;

public sealed class AiMappingAdminIndexViewModel
{
    public string? StatusMessage { get; init; }
    public string? ErrorMessage { get; init; }
    public bool LookupEnabled { get; init; }
    public bool CanLookup { get; init; }
    public bool ShowEditor { get; init; }
    public bool IsCreatingProvider { get; init; }
    public bool EditorHasStoredApiKey { get; init; }
    public AiUsageDashboardViewModel Dashboard { get; init; } = new();
    public IReadOnlyList<AiProviderSummaryViewModel> Providers { get; init; } = [];
    public AiProviderConfigurationInputModel Editor { get; init; } = new();
    public IReadOnlyList<SelectListItem> ProviderTypeOptions { get; init; } = [];
    public IReadOnlyList<SelectListItem> ModelOptions { get; init; } = [];
    public bool SupportsModelDiscovery { get; init; }
    public bool CanLoadModelOptions { get; init; }
    public string? ModelDiscoveryError { get; init; }
    public IReadOnlyList<AiUsageEntryViewModel> RecentUsage { get; init; } = [];
}

public sealed class AiUsageDashboardViewModel
{
    public string ActiveProviderName { get; init; } = "None";
    public string ActiveProviderLabel { get; init; } = "No active provider";
    public int ProviderCount { get; init; }
    public int ConfiguredProviderCount { get; init; }
    public int RequestsToday { get; init; }
    public int RequestsLast7Days { get; init; }
    public int TokensToday { get; init; }
    public int TokensLast7Days { get; init; }
    public int AverageTokensPerRequestLast7Days { get; init; }
    public int TimedOutRequestsToday { get; init; }
    public int TimedOutRequestsLast7Days { get; init; }
    public int CancelledRequestsToday { get; init; }
    public int CancelledRequestsLast7Days { get; init; }
    public int AbortedRequestsToday { get; init; }
    public int AbortedRequestsLast7Days { get; init; }
    public decimal? CostTodaySek { get; init; }
    public decimal? CostLast7DaysSek { get; init; }
    public decimal? CostLast30DaysSek { get; init; }
    public decimal? CostLast365DaysSek { get; init; }
    public DateTime? LastRequestUtc { get; init; }
}

public sealed class AiProviderSummaryViewModel
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public AiProviderType ProviderType { get; init; }
    public string ProviderLabel { get; init; } = string.Empty;
    public string Endpoint { get; init; } = string.Empty;
    public string Model { get; init; } = string.Empty;
    public string? ApiVersion { get; init; }
    public int TimeoutSeconds { get; init; }
    public bool IsActive { get; init; }
    public bool IsConfigured { get; init; }
    public bool HasSavedApiKey { get; init; }
    public string SavedApiKeyDisplay { get; init; } = "Not stored";
    public decimal? InputCostPerMillionTokensSek { get; init; }
    public decimal? OutputCostPerMillionTokensSek { get; init; }
    public int RequestsLast7Days { get; init; }
    public int TokensLast7Days { get; init; }
    public decimal? TotalCostLast7DaysSek { get; init; }
}

public sealed class AiProviderConfigurationInputModel : IValidatableObject
{
    public int? Id { get; set; }

    [Required, StringLength(120)]
    [Display(Name = "Configuration name")]
    public string Name { get; set; } = string.Empty;

    [Display(Name = "Provider")]
    public AiProviderType ProviderType { get; set; } = AiProviderType.OpenAiApi;

    [StringLength(2048)]
    [Display(Name = "Endpoint")]
    public string Endpoint { get; set; } = string.Empty;

    [StringLength(200)]
    [Display(Name = "Model, deployment, or agent name")]
    public string Model { get; set; } = string.Empty;

    [StringLength(80)]
    [Display(Name = "API version")]
    public string? ApiVersion { get; set; }

    [StringLength(8000)]
    [Display(Name = "System prompt")]
    public string SystemPrompt { get; set; } = string.Empty;

    [StringLength(32000)]
    [Display(Name = "Query / prompt template")]
    public string PromptTemplate { get; set; } = string.Empty;

    [Range(typeof(decimal), "0", "1000000000")]
    [Display(Name = "Input cost per 1 million tokens (SEK)")]
    public decimal? InputCostPerMillionTokensSek { get; set; }

    [Range(typeof(decimal), "0", "1000000000")]
    [Display(Name = "Output cost per 1 million tokens (SEK)")]
    public decimal? OutputCostPerMillionTokensSek { get; set; }

    [StringLength(4000)]
    [DataType(DataType.Password)]
    [Display(Name = "API key")]
    public string? ApiKey { get; set; }

    [Range(1, 3600)]
    [Display(Name = "Lookup timeout (seconds)")]
    public int TimeoutSeconds { get; set; } = AppSettingDefaults.AiMappingTimeoutSeconds;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            yield return new ValidationResult(
                "Enter a configuration name before saving.",
                [nameof(Name)]);
        }

        if (string.IsNullOrWhiteSpace(Endpoint))
        {
            yield return new ValidationResult(
                ProviderType == AiProviderType.AzureAiFoundryAgent
                    ? "Enter a project endpoint before saving."
                    : "Enter a chat or responses endpoint before saving.",
                [nameof(Endpoint)]);
        }

        if (ProviderType == AiProviderType.AzureAiFoundryAgent &&
            string.IsNullOrWhiteSpace(Model))
        {
            yield return new ValidationResult(
                "Enter an agent name before saving.",
                [nameof(Model)]);
        }
    }
}

public sealed class AiUsageEntryViewModel
{
    public DateTime OccurredUtc { get; init; }
    public string ProviderName { get; init; } = string.Empty;
    public string ProviderLabel { get; init; } = string.Empty;
    public string Model { get; init; } = string.Empty;
    public string RequestSummary { get; init; } = string.Empty;
    public string RequestKind { get; init; } = string.Empty;
    public int? PromptTokens { get; init; }
    public int? CompletionTokens { get; init; }
    public int? TotalTokens { get; init; }
    public bool WasSuccessful { get; init; }
    public AiRequestOutcome Outcome { get; init; } = AiRequestOutcome.Failed;
    public string OutcomeLabel { get; init; } = "Failed";
    public string OutcomeBadgeClass { get; init; } = "text-bg-danger";
    public decimal? EstimatedInputCostSek { get; init; }
    public decimal? EstimatedOutputCostSek { get; init; }
    public decimal? EstimatedTotalCostSek { get; init; }
    public string? ErrorMessage { get; init; }
}
