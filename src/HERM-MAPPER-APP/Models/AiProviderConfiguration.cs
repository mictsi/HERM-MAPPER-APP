namespace HERMMapperApp.Models;

public enum AiProviderType
{
    OpenWebUi = 1,
    OpenAiApi = 2,
    AzureAiFoundry = 3
}

public enum AiRequestOutcome
{
    Success = 1,
    Failed = 2,
    TimedOut = 3,
    Cancelled = 4,
    Aborted = 5
}

public sealed class AiProviderConfiguration
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public AiProviderType ProviderType { get; set; } = AiProviderType.OpenWebUi;

    public string Endpoint { get; set; } = string.Empty;

    public string Model { get; set; } = string.Empty;

    public string? ApiVersion { get; set; }

    public int TimeoutSeconds { get; set; } = AppSettingDefaults.AiMappingTimeoutSeconds;

    public bool IsActive { get; set; }

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

    public ICollection<AiRequestUsageLog> UsageLogs { get; } = [];
}

public sealed class AiRequestUsageLog
{
    public int Id { get; set; }

    public int? AiProviderConfigurationId { get; set; }

    public AiProviderConfiguration? AiProviderConfiguration { get; set; }

    public string ProviderName { get; set; } = string.Empty;

    public AiProviderType ProviderType { get; set; }

    public string Model { get; set; } = string.Empty;

    public string RequestKind { get; set; } = string.Empty;

    public string RequestSummary { get; set; } = string.Empty;

    public int? PromptTokens { get; set; }

    public int? CompletionTokens { get; set; }

    public int? TotalTokens { get; set; }

    public AiRequestOutcome Outcome { get; set; } = AiRequestOutcome.Failed;

    public bool WasSuccessful { get; set; }

    public int DurationMilliseconds { get; set; }

    public string? ErrorMessage { get; set; }

    public DateTime OccurredUtc { get; set; } = DateTime.UtcNow;
}
