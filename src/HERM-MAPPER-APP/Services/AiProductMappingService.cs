using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using HERMMapperApp.Data;
using HERMMapperApp.Infrastructure;
using HERMMapperApp.Models;
using HERMMapperApp.ViewModels;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HERMMapperApp.Services;

public sealed class AiProductMappingService(
    AppDbContext dbContext,
    AppSettingsService appSettingsService,
    ProtectedSettingsService protectedSettingsService,
    AuditLogService auditLogService,
    HttpClient httpClient,
    ILogger<AiProductMappingService> logger)
{
    private const int MaxSuggestions = 8;
    private const int MaxTextCellLength = 240;
    private const int MinTimeoutSeconds = 1;
    private const int MaxTimeoutSeconds = 3600;
    private const int MaxModelDiscoveryTimeoutSeconds = 15;
    private const string AiMappingCategory = "AiMapping";
    private const string SuggestMappingsRequestKind = "SuggestProductTrmMappings";
    private const string SystemPrompt =
        """
        You map software products to HERM TRM components.
        Use only the TOON data in the user message.
        Never invent component ids.
        A product can map to multiple TRM components.
        Skip components that are already mapped.
        Return only TOON in this exact shape:
        summary: "one short sentence"
        suggestions[N]{component_id	confidence	reason}:
          123	0.95	One short sentence with no tabs or newlines.
        If there are no strong matches, return suggestions[0]{component_id	confidence	reason}: with no rows.
        """;
    private static readonly Action<ILogger, string, Exception?> LogModelDiscoveryTimedOut =
        LoggerMessage.Define<string>(
            LogLevel.Warning,
            new EventId(1, nameof(LogModelDiscoveryTimedOut)),
            "AI model discovery timed out for provider {ProviderName}.");
    private static readonly Action<ILogger, string, Exception?> LogModelDiscoveryFailed =
        LoggerMessage.Define<string>(
            LogLevel.Warning,
            new EventId(2, nameof(LogModelDiscoveryFailed)),
            "AI model discovery failed for provider {ProviderName}.");
    private static readonly Action<ILogger, string, Exception?> LogMappingLookupTimedOut =
        LoggerMessage.Define<string>(
            LogLevel.Warning,
            new EventId(3, nameof(LogMappingLookupTimedOut)),
            "AI mapping lookup timed out for product {ProductName}.");
    private static readonly Action<ILogger, string, Exception?> LogMappingLookupCancelledOrAborted =
        LoggerMessage.Define<string>(
            LogLevel.Warning,
            new EventId(4, nameof(LogMappingLookupCancelledOrAborted)),
            "AI mapping lookup was cancelled or aborted for product {ProductName}.");
    private static readonly Action<ILogger, string, Exception?> LogMappingLookupFailed =
        LoggerMessage.Define<string>(
            LogLevel.Warning,
            new EventId(5, nameof(LogMappingLookupFailed)),
            "AI mapping lookup failed for product {ProductName}.");

    public const string SectionKey = "ai-mapping";

    public async Task<AiProductMappingSettingsSnapshot> GetSettingsAsync(CancellationToken cancellationToken = default)
    {
        await EnsureLegacyConfigurationMigratedAsync(cancellationToken);

        var isEnabled = await GetLookupEnabledAsync(cancellationToken);
        var provider = await GetActiveProviderEntityAsync(asTracking: false, cancellationToken);
        if (provider is null)
        {
            return new AiProductMappingSettingsSnapshot
            {
                IsEnabled = isEnabled
            };
        }

        var apiKey = await protectedSettingsService.GetValueAsync(BuildProviderApiKeySettingKey(provider.Id), cancellationToken);

        return new AiProductMappingSettingsSnapshot
        {
            ActiveProviderId = provider.Id,
            ActiveProviderName = provider.Name,
            ActiveProviderType = provider.ProviderType,
            Endpoint = provider.Endpoint.Trim(),
            Model = provider.Model.Trim(),
            ApiVersion = provider.ApiVersion?.Trim(),
            ApiKey = apiKey,
            IsEnabled = isEnabled,
            TimeoutSeconds = NormalizeTimeoutSeconds(provider.TimeoutSeconds)
        };
    }

    public async Task<AiMappingAdminIndexViewModel> BuildAdminViewModelAsync(
        int? editProviderId = null,
        bool createNewProvider = false,
        AiProviderConfigurationInputModel? editorOverride = null,
        string? statusMessage = null,
        string? errorMessage = null,
        CancellationToken cancellationToken = default)
    {
        await EnsureLegacyConfigurationMigratedAsync(cancellationToken);

        var settings = await GetSettingsAsync(cancellationToken);
        var providers = await dbContext.AiProviderConfigurations
            .AsNoTracking()
            .OrderBy(x => x.Name)
            .ThenBy(x => x.Id)
            .ToListAsync(cancellationToken);

        var sevenDaysAgoUtc = DateTime.UtcNow.Date.AddDays(-6);
        var usageStats = await dbContext.AiRequestUsageLogs
            .AsNoTracking()
            .Where(x => x.AiProviderConfigurationId.HasValue && x.OccurredUtc >= sevenDaysAgoUtc)
            .GroupBy(x => x.AiProviderConfigurationId)
            .Select(group => new
            {
                ProviderId = group.Key!.Value,
                Requests = group.Count(),
                Tokens = group.Sum(x => x.TotalTokens ?? 0)
            })
            .ToDictionaryAsync(x => x.ProviderId, cancellationToken);

        var providerSummaries = new List<AiProviderSummaryViewModel>();
        foreach (var provider in providers)
        {
            var apiKey = await protectedSettingsService.GetValueAsync(BuildProviderApiKeySettingKey(provider.Id), cancellationToken);
            usageStats.TryGetValue(provider.Id, out var usage);

            providerSummaries.Add(new AiProviderSummaryViewModel
            {
                Id = provider.Id,
                Name = provider.Name,
                ProviderType = provider.ProviderType,
                ProviderLabel = GetProviderLabel(provider.ProviderType),
                Endpoint = provider.Endpoint,
                Model = provider.Model,
                ApiVersion = provider.ApiVersion,
                TimeoutSeconds = NormalizeTimeoutSeconds(provider.TimeoutSeconds),
                IsActive = provider.IsActive,
                IsConfigured = IsProviderConfigured(provider.Endpoint, provider.Model, apiKey),
                HasSavedApiKey = !string.IsNullOrWhiteSpace(apiKey),
                SavedApiKeyDisplay = string.IsNullOrWhiteSpace(apiKey) ? "Not stored" : $"Stored ({apiKey.Length} chars)",
                RequestsLast7Days = usage?.Requests ?? 0,
                TokensLast7Days = usage?.Tokens ?? 0
            });
        }

        var editorEntity = ResolveEditorProvider(providers, editProviderId);
        var showEditor = editorOverride is not null || createNewProvider || editorEntity is not null;
        var isCreatingProvider = showEditor && !EditorModelExists(editorOverride, editorEntity);
        var editorModel = editorOverride ?? (showEditor ? BuildEditorInputModel(editorEntity) : BuildEditorInputModel(null));
        var supportsModelDiscovery = showEditor && SupportsModelDiscovery(editorModel.ProviderType);
        var editorHasStoredApiKey = showEditor &&
            editorEntity is not null &&
            !string.IsNullOrWhiteSpace(await protectedSettingsService.GetValueAsync(
                BuildProviderApiKeySettingKey(editorEntity.Id),
                cancellationToken));

        List<SelectListItem> modelOptions = [];
        string? modelDiscoveryError = null;
        if (showEditor && editorModel.Id.HasValue && supportsModelDiscovery)
        {
            var modelDiscovery = await GetAvailableModelsAsync(editorModel.Id.Value, cancellationToken);
            if (modelDiscovery.IsSuccess)
            {
                modelOptions =
                [
                    new SelectListItem
                    {
                        Value = string.Empty,
                        Text = "Choose a model",
                        Selected = string.IsNullOrWhiteSpace(editorModel.Model)
                    },
                    .. modelDiscovery.Models
                    .Select(model => new SelectListItem
                    {
                        Value = model,
                        Text = model,
                        Selected = string.Equals(model, editorModel.Model, StringComparison.Ordinal)
                    })
                ];
            }
            else
            {
                modelDiscoveryError = modelDiscovery.Message;
            }
        }

        var todayUtc = DateTime.UtcNow.Date;
        var usageWindowLogs = await dbContext.AiRequestUsageLogs
            .AsNoTracking()
            .Where(x => x.OccurredUtc >= sevenDaysAgoUtc)
            .ToListAsync(cancellationToken);
        var recentUsageLogs = await dbContext.AiRequestUsageLogs
            .AsNoTracking()
            .OrderByDescending(x => x.OccurredUtc)
            .Take(40)
            .ToListAsync(cancellationToken);

        var requestsToday = usageWindowLogs.Count(x => x.OccurredUtc >= todayUtc);
        var requestsLast7Days = usageWindowLogs.Count;
        var tokensToday = usageWindowLogs
            .Where(x => x.OccurredUtc >= todayUtc)
            .Sum(x => x.TotalTokens ?? 0);
        var tokensLast7Days = usageWindowLogs.Sum(x => x.TotalTokens ?? 0);
        var successfulLast7Days = usageWindowLogs
            .Where(x => x.Outcome == AiRequestOutcome.Success && x.TotalTokens.HasValue)
            .ToList();
        var averageTokensPerRequestLast7Days = successfulLast7Days.Count == 0
            ? 0
            : (int)Math.Round(successfulLast7Days.Average(x => (double)x.TotalTokens!.Value), MidpointRounding.AwayFromZero);
        var timedOutRequestsToday = usageWindowLogs.Count(x => x.OccurredUtc >= todayUtc && x.Outcome == AiRequestOutcome.TimedOut);
        var timedOutRequestsLast7Days = usageWindowLogs.Count(x => x.Outcome == AiRequestOutcome.TimedOut);
        var cancelledRequestsToday = usageWindowLogs.Count(x => x.OccurredUtc >= todayUtc && x.Outcome == AiRequestOutcome.Cancelled);
        var cancelledRequestsLast7Days = usageWindowLogs.Count(x => x.Outcome == AiRequestOutcome.Cancelled);
        var abortedRequestsToday = usageWindowLogs.Count(x => x.OccurredUtc >= todayUtc && x.Outcome == AiRequestOutcome.Aborted);
        var abortedRequestsLast7Days = usageWindowLogs.Count(x => x.Outcome == AiRequestOutcome.Aborted);

        return new AiMappingAdminIndexViewModel
        {
            StatusMessage = statusMessage,
            ErrorMessage = errorMessage,
            LookupEnabled = settings.IsEnabled,
            CanLookup = settings.CanLookup,
            ShowEditor = showEditor,
            IsCreatingProvider = isCreatingProvider,
            EditorHasStoredApiKey = editorHasStoredApiKey,
            Dashboard = new AiUsageDashboardViewModel
            {
                ActiveProviderName = settings.ActiveProviderName ?? "None",
                ActiveProviderLabel = settings.ActiveProviderType is null
                    ? "Choose a provider configuration to enable AI-assisted mapping."
                    : GetProviderLabel(settings.ActiveProviderType.Value),
                ProviderCount = providerSummaries.Count,
                ConfiguredProviderCount = providerSummaries.Count(x => x.IsConfigured),
                RequestsToday = requestsToday,
                RequestsLast7Days = requestsLast7Days,
                TokensToday = tokensToday,
                TokensLast7Days = tokensLast7Days,
                AverageTokensPerRequestLast7Days = averageTokensPerRequestLast7Days,
                TimedOutRequestsToday = timedOutRequestsToday,
                TimedOutRequestsLast7Days = timedOutRequestsLast7Days,
                CancelledRequestsToday = cancelledRequestsToday,
                CancelledRequestsLast7Days = cancelledRequestsLast7Days,
                AbortedRequestsToday = abortedRequestsToday,
                AbortedRequestsLast7Days = abortedRequestsLast7Days,
                LastRequestUtc = recentUsageLogs.FirstOrDefault()?.OccurredUtc
            },
            Providers = providerSummaries,
            Editor = editorModel,
            ProviderTypeOptions = BuildProviderTypeOptions(editorModel.ProviderType),
            ModelOptions = modelOptions,
            SupportsModelDiscovery = supportsModelDiscovery,
            ModelDiscoveryError = modelDiscoveryError,
            RecentUsage = recentUsageLogs
                .Select(log => new AiUsageEntryViewModel
                {
                    OccurredUtc = log.OccurredUtc,
                    ProviderName = log.ProviderName,
                    ProviderLabel = GetProviderLabel(log.ProviderType),
                    Model = log.Model,
                    RequestSummary = log.RequestSummary,
                    RequestKind = log.RequestKind,
                    PromptTokens = log.PromptTokens,
                    CompletionTokens = log.CompletionTokens,
                    TotalTokens = log.TotalTokens,
                    WasSuccessful = log.WasSuccessful,
                    Outcome = log.Outcome,
                    OutcomeLabel = GetOutcomeLabel(log.Outcome),
                    OutcomeBadgeClass = GetOutcomeBadgeClass(log.Outcome),
                    ErrorMessage = log.ErrorMessage
                })
                .ToList()
        };
    }

    public async Task<AiProviderSaveResult> SaveProviderAsync(
        AiProviderConfigurationInputModel input,
        CancellationToken cancellationToken = default)
    {
        await EnsureLegacyConfigurationMigratedAsync(cancellationToken);

        NormalizeProviderInput(input);

        var existingProvider = input.Id.HasValue
            ? await dbContext.AiProviderConfigurations.FirstOrDefaultAsync(x => x.Id == input.Id.Value, cancellationToken)
            : null;

        if (input.Id.HasValue && existingProvider is null)
        {
            return AiProviderSaveResult.Failure("The selected AI configuration no longer exists.");
        }

        var endpoint = input.Endpoint.Trim();
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var endpointUri) ||
            (endpointUri.Scheme != Uri.UriSchemeHttp && endpointUri.Scheme != Uri.UriSchemeHttps))
        {
            return AiProviderSaveResult.Failure("Enter a valid HTTP or HTTPS chat completion endpoint.");
        }

        if (string.IsNullOrWhiteSpace(input.Name))
        {
            return AiProviderSaveResult.Failure("Enter a configuration name before saving.");
        }

        if (input.TimeoutSeconds < MinTimeoutSeconds || input.TimeoutSeconds > MaxTimeoutSeconds)
        {
            return AiProviderSaveResult.Failure($"Enter an AI lookup timeout between {MinTimeoutSeconds} and {MaxTimeoutSeconds} seconds.");
        }

        var existingApiKey = existingProvider is null
            ? null
            : await protectedSettingsService.GetValueAsync(BuildProviderApiKeySettingKey(existingProvider.Id), cancellationToken);
        var apiKey = string.IsNullOrWhiteSpace(input.ApiKey)
            ? existingApiKey
            : input.ApiKey.Trim();

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return AiProviderSaveResult.Failure("Enter an API key before saving.");
        }

        var caseInsensitiveCollation = AppDatabaseCollations.GetCaseInsensitive(dbContext.Database);
        var duplicateNameExists = await dbContext.AiProviderConfigurations.AnyAsync(
            x => (!input.Id.HasValue || x.Id != input.Id.Value) &&
                 EF.Functions.Collate(x.Name, caseInsensitiveCollation) == input.Name,
            cancellationToken);

        if (duplicateNameExists)
        {
            return AiProviderSaveResult.Failure($"An AI configuration named '{input.Name}' already exists.");
        }

        var provider = existingProvider ?? new AiProviderConfiguration
        {
            CreatedUtc = DateTime.UtcNow
        };

        provider.Name = input.Name;
        provider.ProviderType = input.ProviderType;
        provider.Endpoint = endpoint;
        provider.Model = input.Model;
        provider.ApiVersion = NormalizeApiVersion(input.ProviderType, input.ApiVersion);
        provider.TimeoutSeconds = NormalizeTimeoutSeconds(input.TimeoutSeconds);
        provider.UpdatedUtc = DateTime.UtcNow;

        if (existingProvider is null)
        {
            provider.IsActive = !await dbContext.AiProviderConfigurations.AnyAsync(cancellationToken);
            dbContext.AiProviderConfigurations.Add(provider);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await protectedSettingsService.SetValueAsync(BuildProviderApiKeySettingKey(provider.Id), apiKey, cancellationToken);
        var requiresModelSelection = string.IsNullOrWhiteSpace(provider.Model);

        await auditLogService.WriteAsync(
            AiMappingCategory,
            existingProvider is null ? "CreateProvider" : "UpdateProvider",
            nameof(AiProviderConfiguration),
            provider.Id,
            $"{(existingProvider is null ? "Created" : "Updated")} AI provider configuration '{provider.Name}'.",
            $"Provider: {GetProviderLabel(provider.ProviderType)}; endpoint: {provider.Endpoint}; model: {provider.Model}; timeout seconds: {provider.TimeoutSeconds}.",
            cancellationToken);

        var successMessage = "AI provider configuration updated.";
        if (requiresModelSelection)
        {
            successMessage = "AI provider configuration saved. Choose a model from the dropdown to finish setup.";
        }
        else if (existingProvider is null)
        {
            successMessage = "AI provider configuration saved.";
        }

        return AiProviderSaveResult.Success(successMessage, provider.Id);
    }

    public async Task<AiOperationResult> SetLookupEnabledAsync(bool isEnabled, CancellationToken cancellationToken = default)
    {
        await EnsureLegacyConfigurationMigratedAsync(cancellationToken);

        if (isEnabled)
        {
            var settings = await GetSettingsAsync(cancellationToken);
            if (!settings.IsConfigured)
            {
                return AiOperationResult.Failure("Save and enable a provider with endpoint, model, and API key before enabling AI mapping lookup.");
            }
        }

        await appSettingsService.SetValueAsync(AppSettingKeys.AiMappingIsEnabled, isEnabled.ToString(), cancellationToken);
        await auditLogService.WriteAsync(
            AiMappingCategory,
            isEnabled ? "EnableLookup" : "DisableLookup",
            nameof(AppSetting),
            null,
            isEnabled ? "Enabled AI mapping lookup." : "Disabled AI mapping lookup.",
            isEnabled
                ? "The Add mappings with AI action can be used again."
                : "The Add mappings with AI action is blocked until lookup is enabled again.",
            cancellationToken);

        return AiOperationResult.Success(
            isEnabled
                ? "AI mapping lookup enabled."
                : "AI mapping lookup disabled. The Add mappings with AI button will stay unavailable until it is enabled again.");
    }

    public async Task<AiOperationResult> SetProviderEnabledAsync(
        int providerId,
        bool isEnabled,
        CancellationToken cancellationToken = default)
    {
        await EnsureLegacyConfigurationMigratedAsync(cancellationToken);

        var providers = await dbContext.AiProviderConfigurations
            .OrderBy(x => x.Id)
            .ToListAsync(cancellationToken);

        var selectedProvider = providers.FirstOrDefault(x => x.Id == providerId);
        if (selectedProvider is null)
        {
            return AiOperationResult.Failure("The selected AI provider configuration no longer exists.");
        }

        var nowUtc = DateTime.UtcNow;
        var hasChanges = false;
        foreach (var provider in providers)
        {
            var shouldBeActive = isEnabled && provider.Id == providerId;
            if (provider.IsActive == shouldBeActive)
            {
                continue;
            }

            provider.IsActive = shouldBeActive;
            provider.UpdatedUtc = nowUtc;
            hasChanges = true;
        }

        if (hasChanges)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        await auditLogService.WriteAsync(
            AiMappingCategory,
            isEnabled ? "EnableProvider" : "DisableProvider",
            nameof(AiProviderConfiguration),
            selectedProvider.Id,
            isEnabled
                ? $"Enabled AI provider configuration '{selectedProvider.Name}'."
                : $"Disabled AI provider configuration '{selectedProvider.Name}'.",
            isEnabled
                ? $"Provider: {GetProviderLabel(selectedProvider.ProviderType)}; endpoint: {selectedProvider.Endpoint}. Other providers were disabled."
                : $"Provider: {GetProviderLabel(selectedProvider.ProviderType)}; endpoint: {selectedProvider.Endpoint}.",
            cancellationToken);

        return AiOperationResult.Success(
            isEnabled
                ? $"'{selectedProvider.Name}' is now enabled. Other providers were disabled."
                : $"'{selectedProvider.Name}' is now disabled.");
    }

    public async Task<AiOperationResult> DeleteProviderAsync(int providerId, CancellationToken cancellationToken = default)
    {
        await EnsureLegacyConfigurationMigratedAsync(cancellationToken);

        var provider = await dbContext.AiProviderConfigurations.FirstOrDefaultAsync(x => x.Id == providerId, cancellationToken);
        if (provider is null)
        {
            return AiOperationResult.Failure("The selected AI provider configuration no longer exists.");
        }

        var providerName = provider.Name;
        var wasActive = provider.IsActive;
        dbContext.AiProviderConfigurations.Remove(provider);
        await dbContext.SaveChangesAsync(cancellationToken);
        await protectedSettingsService.SetValueAsync(BuildProviderApiKeySettingKey(providerId), null, cancellationToken);

        await auditLogService.WriteAsync(
            AiMappingCategory,
            "DeleteProvider",
            nameof(AiProviderConfiguration),
            providerId,
            $"Deleted AI provider configuration '{providerName}'.",
            wasActive
                ? "The enabled provider was removed."
                : "A non-active provider was removed.",
            cancellationToken);

        return AiOperationResult.Success($"Deleted AI provider configuration '{providerName}'.");
    }

    public async Task<AiModelDiscoveryResult> GetAvailableModelsAsync(int providerId, CancellationToken cancellationToken = default)
    {
        await EnsureLegacyConfigurationMigratedAsync(cancellationToken);

        var provider = await dbContext.AiProviderConfigurations
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == providerId, cancellationToken);

        if (provider is null)
        {
            return AiModelDiscoveryResult.Failure("The selected AI provider configuration no longer exists.");
        }

        if (!SupportsModelDiscovery(provider.ProviderType))
        {
            return AiModelDiscoveryResult.Failure("Model discovery is only available for providers that expose a model listing endpoint.");
        }

        var apiKey = await protectedSettingsService.GetValueAsync(BuildProviderApiKeySettingKey(provider.Id), cancellationToken);
        if (string.IsNullOrWhiteSpace(provider.Endpoint) || string.IsNullOrWhiteSpace(apiKey))
        {
            return AiModelDiscoveryResult.Failure("Save the endpoint and API key before loading available models.");
        }

        if (!TryBuildModelsEndpoint(provider, out var modelsEndpoint))
        {
            return AiModelDiscoveryResult.Failure("This provider type does not expose a supported model listing endpoint.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, modelsEndpoint);
        ApplyAuthentication(request, provider.ProviderType, apiKey!);

        var timeoutSeconds = Math.Min(MaxModelDiscoveryTimeoutSeconds, NormalizeTimeoutSeconds(provider.TimeoutSeconds));
        using var timeoutCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCancellationTokenSource.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

        try
        {
            using var response = await SendRequestAsync(
                request,
                $"model discovery for provider '{provider.Name}'",
                timeoutSeconds,
                cancellationToken,
                timeoutCancellationTokenSource.Token);
            var responseBody = await response.Content.ReadAsStringAsync(timeoutCancellationTokenSource.Token);

            if (!response.IsSuccessStatusCode)
            {
                return AiModelDiscoveryResult.Failure(
                    $"Model discovery failed with status {(int)response.StatusCode}: {responseBody}");
            }

            var models = ParseAvailableModels(responseBody);
            return models.Count == 0
                ? AiModelDiscoveryResult.Failure("The provider returned no models.")
                : AiModelDiscoveryResult.Success(models);
        }
        catch (TimeoutException exception)
        {
            LogModelDiscoveryTimedOut(logger, provider.Name, exception);
            return AiModelDiscoveryResult.Failure(
                $"AI model discovery timed out after {timeoutSeconds} second{(timeoutSeconds == 1 ? string.Empty : "s")}.");
        }
        catch (InvalidOperationException exception)
        {
            LogModelDiscoveryFailed(logger, provider.Name, exception);
            return AiModelDiscoveryResult.Failure(exception.Message);
        }
    }

    public async Task<AiProductMappingSuggestionResult> SuggestMappingsAsync(
        ProductCatalogItem product,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(product);

        await EnsureLegacyConfigurationMigratedAsync(cancellationToken);

        var settings = await GetSettingsAsync(cancellationToken);
        if (!settings.IsEnabled)
        {
            return AiProductMappingSuggestionResult.Failure("AI mapping lookup is disabled in configuration.");
        }

        if (!settings.IsConfigured)
        {
            return AiProductMappingSuggestionResult.Failure("AI mapping lookup is not configured yet.");
        }

        var startedUtc = DateTime.UtcNow;
        var stopwatch = Stopwatch.StartNew();
        ParsedAiUsage? usage = null;
        string? errorMessage = null;
        var outcome = AiRequestOutcome.Failed;
        var requestDispatched = false;
        CancellationTokenSource? timeoutCancellationTokenSource = null;

        try
        {
            var components = await dbContext.TrmComponents
                .AsNoTracking()
                .Where(x => !x.IsDeleted)
                .Include(x => x.ParentCapability)
                .ThenInclude(x => x!.ParentDomain)
                .OrderBy(x => x.ParentCapability!.ParentDomain!.Code)
                .ThenBy(x => x.ParentCapability!.Code)
                .ThenBy(x => x.TechnologyComponentCode ?? x.Code)
                .ToListAsync(cancellationToken);

            using var request = new HttpRequestMessage(HttpMethod.Post, BuildChatCompletionEndpoint(settings))
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(new
                    {
                        model = settings.Model,
                        messages = new object[]
                        {
                            new
                            {
                                role = "system",
                                content = SystemPrompt
                            },
                            new
                            {
                                role = "user",
                                content = BuildUserPrompt(product, components)
                            }
                        }
                    }),
                    Encoding.UTF8,
                    "application/json")
            };
            ApplyAuthentication(request, settings.ActiveProviderType!.Value, settings.ApiKey!);

            timeoutCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCancellationTokenSource.CancelAfter(TimeSpan.FromSeconds(settings.TimeoutSeconds));

            requestDispatched = true;
            using var response = await SendRequestAsync(
                request,
                $"mapping lookup for product '{product.Name}'",
                settings.TimeoutSeconds,
                cancellationToken,
                timeoutCancellationTokenSource.Token);

            var responseBody = await response.Content.ReadAsStringAsync(timeoutCancellationTokenSource.Token);
            usage = ParseUsage(responseBody);

            if (!response.IsSuccessStatusCode)
            {
                errorMessage = $"AI mapping lookup failed with status {(int)response.StatusCode}: {responseBody}";
                outcome = AiRequestOutcome.Failed;
                return AiProductMappingSuggestionResult.Failure(errorMessage);
            }

            var rawContent = ExtractAssistantContent(responseBody);
            if (string.IsNullOrWhiteSpace(rawContent))
            {
                errorMessage = "AI mapping lookup completed, but the response did not include any assistant content.";
                outcome = AiRequestOutcome.Failed;
                return AiProductMappingSuggestionResult.Failure(errorMessage);
            }

            var parsedSuggestions = ParseSuggestions(rawContent);
            var existingComponentIds = product.Mappings
                .Where(x => x.TrmComponentId.HasValue)
                .Select(x => x.TrmComponentId!.Value)
                .ToHashSet();

            var suggestedIds = parsedSuggestions
                .Select(x => x.ComponentId)
                .Distinct()
                .Where(id => !existingComponentIds.Contains(id))
                .Take(MaxSuggestions)
                .ToList();

            if (suggestedIds.Count == 0)
            {
                outcome = AiRequestOutcome.Success;
                return AiProductMappingSuggestionResult.Success(
                    ExtractSummary(rawContent) ?? "No new TRM component suggestions were returned.",
                    []);
            }

            var componentLookup = await dbContext.TrmComponents
                .AsNoTracking()
                .Where(x => suggestedIds.Contains(x.Id) && !x.IsDeleted)
                .Include(x => x.ParentCapability)
                .ThenInclude(x => x!.ParentDomain)
                .ToDictionaryAsync(x => x.Id, timeoutCancellationTokenSource.Token);

            var suggestions = new List<AiProductMappingSuggestion>();
            foreach (var parsedSuggestion in parsedSuggestions)
            {
                if (!componentLookup.TryGetValue(parsedSuggestion.ComponentId, out var component) ||
                    component.ParentCapability?.ParentDomain is null)
                {
                    continue;
                }

                if (existingComponentIds.Contains(component.Id) ||
                    suggestions.Any(existing => existing.ComponentId == component.Id))
                {
                    continue;
                }

                suggestions.Add(new AiProductMappingSuggestion
                {
                    ComponentId = component.Id,
                    DomainLabel = $"{component.ParentCapability.ParentDomain.Code} {component.ParentCapability.ParentDomain.Name}",
                    CapabilityLabel = $"{component.ParentCapability.Code} {component.ParentCapability.Name}",
                    ComponentLabel = component.DisplayLabel,
                    Confidence = parsedSuggestion.Confidence,
                    Reason = parsedSuggestion.Reason
                });
            }

            outcome = AiRequestOutcome.Success;
            return AiProductMappingSuggestionResult.Success(
                ExtractSummary(rawContent) ?? $"AI suggested {suggestions.Count} TRM component mapping(s).",
                suggestions);
        }
        catch (TimeoutException exception)
        {
            LogMappingLookupTimedOut(logger, product.Name, exception);
            errorMessage = $"AI mapping lookup timed out after {settings.TimeoutSeconds} second{(settings.TimeoutSeconds == 1 ? string.Empty : "s")}.";
            outcome = AiRequestOutcome.TimedOut;
            return AiProductMappingSuggestionResult.Failure(errorMessage);
        }
        catch (OperationCanceledException exception)
            when (!cancellationToken.IsCancellationRequested &&
                  timeoutCancellationTokenSource?.IsCancellationRequested == true)
        {
            LogMappingLookupTimedOut(logger, product.Name, exception);
            errorMessage = $"AI mapping lookup timed out after {settings.TimeoutSeconds} second{(settings.TimeoutSeconds == 1 ? string.Empty : "s")}.";
            outcome = AiRequestOutcome.TimedOut;
            return AiProductMappingSuggestionResult.Failure(errorMessage);
        }
        catch (OperationCanceledException exception) when (cancellationToken.IsCancellationRequested)
        {
            LogMappingLookupCancelledOrAborted(logger, product.Name, exception);
            SetCancellationOutcome(requestDispatched, out errorMessage, out outcome);
            throw;
        }
        catch (InvalidOperationException exception)
        {
            LogMappingLookupFailed(logger, product.Name, exception);
            errorMessage = exception.Message;
            outcome = AiRequestOutcome.Aborted;
            return AiProductMappingSuggestionResult.Failure(errorMessage);
        }
        finally
        {
            timeoutCancellationTokenSource?.Dispose();
            await WriteUsageLogAsync(
                settings,
                startedUtc,
                stopwatch.Elapsed,
                usage,
                outcome,
                $"{SuggestMappingsRequestKind}: {product.Name}",
                SuggestMappingsRequestKind,
                errorMessage,
                CancellationToken.None);
        }
    }

    public static string GetProviderLabel(AiProviderType providerType) =>
        providerType switch
        {
            AiProviderType.OpenAiApi => "OpenAI API",
            AiProviderType.AzureAiFoundry => "Azure AI Foundry",
            _ => "Open WebUI"
        };

    private async Task EnsureLegacyConfigurationMigratedAsync(CancellationToken cancellationToken)
    {
        var providers = await dbContext.AiProviderConfigurations
            .OrderBy(x => x.Id)
            .ToListAsync(cancellationToken);

        if (providers.Count > 0)
        {
            return;
        }

        var legacyEndpoint = await appSettingsService.GetNullableValueAsync(AppSettingKeys.AiMappingEndpoint, cancellationToken);
        var legacyModel = await appSettingsService.GetNullableValueAsync(AppSettingKeys.AiMappingModel, cancellationToken);
        var legacyApiKey = await protectedSettingsService.GetValueAsync(AppSettingKeys.AiMappingApiKey, cancellationToken);
        var legacyTimeoutValue = await appSettingsService.GetNullableValueAsync(AppSettingKeys.AiMappingTimeoutSeconds, cancellationToken);

        if (string.IsNullOrWhiteSpace(legacyEndpoint) &&
            string.IsNullOrWhiteSpace(legacyModel) &&
            string.IsNullOrWhiteSpace(legacyApiKey))
        {
            return;
        }

        var provider = new AiProviderConfiguration
        {
            Name = "Open WebUI (Migrated)",
            ProviderType = AiProviderType.OpenWebUi,
            Endpoint = string.IsNullOrWhiteSpace(legacyEndpoint) ? AppSettingDefaults.AiMappingEndpoint : legacyEndpoint.Trim(),
            Model = string.IsNullOrWhiteSpace(legacyModel) ? AppSettingDefaults.AiMappingModel : legacyModel.Trim(),
            TimeoutSeconds = TryParseTimeoutSeconds(legacyTimeoutValue),
            IsActive = true,
            CreatedUtc = DateTime.UtcNow,
            UpdatedUtc = DateTime.UtcNow
        };

        dbContext.AiProviderConfigurations.Add(provider);
        await dbContext.SaveChangesAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(legacyApiKey))
        {
            await protectedSettingsService.SetValueAsync(BuildProviderApiKeySettingKey(provider.Id), legacyApiKey, cancellationToken);
        }

        await auditLogService.WriteAsync(
            AiMappingCategory,
            "MigrateLegacyConfiguration",
            nameof(AiProviderConfiguration),
            provider.Id,
            "Migrated legacy AI mapping settings into a provider configuration.",
            $"Endpoint: {provider.Endpoint}; model: {provider.Model}; timeout seconds: {provider.TimeoutSeconds}.",
            cancellationToken);
    }

    private async Task<bool> GetLookupEnabledAsync(CancellationToken cancellationToken) =>
        bool.TryParse(
            await appSettingsService.GetValueAsync(
                AppSettingKeys.AiMappingIsEnabled,
                AppSettingDefaults.AiMappingEnabled.ToString(),
                cancellationToken),
            out var parsedIsEnabled)
            ? parsedIsEnabled
            : AppSettingDefaults.AiMappingEnabled;

    private async Task<AiProviderConfiguration?> GetActiveProviderEntityAsync(bool asTracking, CancellationToken cancellationToken)
    {
        var query = dbContext.AiProviderConfigurations.AsQueryable();
        if (!asTracking)
        {
            query = query.AsNoTracking();
        }

        return await query
            .OrderBy(x => x.Name)
            .ThenBy(x => x.Id)
            .FirstOrDefaultAsync(x => x.IsActive, cancellationToken);
    }

    private static AiProviderConfiguration? ResolveEditorProvider(IReadOnlyList<AiProviderConfiguration> providers, int? editProviderId)
    {
        return editProviderId.HasValue
            ? providers.FirstOrDefault(x => x.Id == editProviderId.Value)
            : null;
    }

    private static bool EditorModelExists(
        AiProviderConfigurationInputModel? editorOverride,
        AiProviderConfiguration? editorEntity) =>
        editorOverride?.Id.HasValue == true || editorEntity is not null;

    private static AiProviderConfigurationInputModel BuildEditorInputModel(AiProviderConfiguration? provider) =>
        provider is null
            ? new AiProviderConfigurationInputModel
            {
                Name = "Open WebUI",
                ProviderType = AiProviderType.OpenWebUi,
                Endpoint = AppSettingDefaults.AiMappingEndpoint,
                Model = AppSettingDefaults.AiMappingModel,
                TimeoutSeconds = AppSettingDefaults.AiMappingTimeoutSeconds
            }
            : new AiProviderConfigurationInputModel
            {
                Id = provider.Id,
                Name = provider.Name,
                ProviderType = provider.ProviderType,
                Endpoint = provider.Endpoint,
                Model = provider.Model,
                ApiVersion = provider.ApiVersion,
                TimeoutSeconds = NormalizeTimeoutSeconds(provider.TimeoutSeconds)
            };

    private static List<SelectListItem> BuildProviderTypeOptions(AiProviderType selectedProviderType) =>
        Enum.GetValues<AiProviderType>()
            .Select(providerType => new SelectListItem
            {
                Value = providerType.ToString(),
                Text = GetProviderLabel(providerType),
                Selected = providerType == selectedProviderType
            })
            .ToList();

    private static void NormalizeProviderInput(AiProviderConfigurationInputModel input)
    {
        input.Name = input.Name?.Trim() ?? string.Empty;
        input.Endpoint = input.Endpoint?.Trim() ?? string.Empty;
        input.Model = input.Model?.Trim() ?? string.Empty;
        input.ApiVersion = string.IsNullOrWhiteSpace(input.ApiVersion) ? null : input.ApiVersion.Trim();
        input.ApiKey = string.IsNullOrWhiteSpace(input.ApiKey) ? null : input.ApiKey.Trim();
        input.TimeoutSeconds = NormalizeTimeoutSeconds(input.TimeoutSeconds);
    }

    private static string? NormalizeApiVersion(AiProviderType providerType, string? apiVersion) =>
        providerType == AiProviderType.AzureAiFoundry && !string.IsNullOrWhiteSpace(apiVersion)
            ? apiVersion.Trim()
            : null;

    private static bool IsProviderConfigured(string endpoint, string model, string? apiKey) =>
        !string.IsNullOrWhiteSpace(endpoint) &&
        !string.IsNullOrWhiteSpace(model) &&
        !string.IsNullOrWhiteSpace(apiKey);

    private static int NormalizeTimeoutSeconds(int timeoutSeconds) =>
        timeoutSeconds is >= MinTimeoutSeconds and <= MaxTimeoutSeconds
            ? timeoutSeconds
            : AppSettingDefaults.AiMappingTimeoutSeconds;

    private static string BuildProviderApiKeySettingKey(int providerId) =>
        $"AiProvider.{providerId}.ApiKey";

    private static string BuildChatCompletionEndpoint(AiProductMappingSettingsSnapshot settings)
    {
        if (settings.ActiveProviderType != AiProviderType.AzureAiFoundry || string.IsNullOrWhiteSpace(settings.ApiVersion))
        {
            return settings.Endpoint;
        }

        var endpointUri = new Uri(settings.Endpoint, UriKind.Absolute);
        var builder = new UriBuilder(endpointUri);
        var querySegments = builder.Query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Where(segment => !segment.StartsWith("api-version=", StringComparison.OrdinalIgnoreCase))
            .ToList();
        querySegments.Add($"api-version={Uri.EscapeDataString(settings.ApiVersion)}");
        builder.Query = string.Join("&", querySegments);
        return builder.Uri.ToString();
    }

    private static void ApplyAuthentication(HttpRequestMessage request, AiProviderType providerType, string apiKey)
    {
        if (providerType == AiProviderType.AzureAiFoundry)
        {
            request.Headers.TryAddWithoutValidation("api-key", apiKey);
            return;
        }

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
    }

    private static bool SupportsModelDiscovery(AiProviderType providerType) =>
        providerType is AiProviderType.OpenWebUi or AiProviderType.OpenAiApi;

    private static bool TryBuildModelsEndpoint(AiProviderConfiguration provider, out string endpoint)
    {
        endpoint = string.Empty;
        if (!Uri.TryCreate(provider.Endpoint, UriKind.Absolute, out var endpointUri))
        {
            return false;
        }

        var path = provider.ProviderType switch
        {
            AiProviderType.OpenAiApi => BuildOpenAiModelsPath(endpointUri.AbsolutePath),
            AiProviderType.OpenWebUi => "/api/models",
            _ => null
        };

        if (path is null)
        {
            return false;
        }

        var builder = new UriBuilder(endpointUri)
        {
            Path = path,
            Query = string.Empty
        };
        endpoint = builder.Uri.ToString();
        return true;
    }

    private static string BuildOpenAiModelsPath(string currentPath)
    {
        const string chatCompletionsSuffix = "/chat/completions";

        if (currentPath.EndsWith(chatCompletionsSuffix, StringComparison.OrdinalIgnoreCase))
        {
            return currentPath[..^chatCompletionsSuffix.Length] + "/models";
        }

        return "/v1/models";
    }

    private static List<string> ParseAvailableModels(string responseBody)
    {
        using var document = JsonDocument.Parse(responseBody);
        var identifiers = new HashSet<string>(StringComparer.Ordinal);

        if (document.RootElement.ValueKind == JsonValueKind.Array)
        {
            AddModelIdentifiers(document.RootElement, identifiers);
        }
        else if (document.RootElement.ValueKind == JsonValueKind.Object)
        {
            if (document.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
            {
                AddModelIdentifiers(data, identifiers);
            }
            else if (document.RootElement.TryGetProperty("models", out var models) && models.ValueKind == JsonValueKind.Array)
            {
                AddModelIdentifiers(models, identifiers);
            }
        }

        return identifiers
            .OrderBy(model => model, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static void AddModelIdentifiers(JsonElement arrayElement, HashSet<string> identifiers)
    {
        using var enumerator = arrayElement.EnumerateArray();
        while (enumerator.MoveNext())
        {
            var element = enumerator.Current;
            var model = ExtractModelIdentifier(element);
            if (!string.IsNullOrWhiteSpace(model))
            {
                identifiers.Add(model);
            }
        }
    }

    private static string? ExtractModelIdentifier(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.String)
        {
            return element.GetString();
        }

        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (element.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String)
        {
            return id.GetString();
        }

        if (element.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String)
        {
            return name.GetString();
        }

        if (element.TryGetProperty("model", out var model) && model.ValueKind == JsonValueKind.String)
        {
            return model.GetString();
        }

        return null;
    }

    private static string BuildUserPrompt(ProductCatalogItem product, List<TrmComponent> components)
    {
        var builder = new StringBuilder();
        builder.AppendLine("request:");
        builder.AppendLine("  action: \"suggest_product_trm_component_mappings\"");
        builder.AppendLine("  format: \"TOON\"");
        builder.AppendLine("product:");
        builder.AppendLine(string.Create(CultureInfo.InvariantCulture, $"  id: {product.Id}"));
        builder.AppendLine(string.Create(CultureInfo.InvariantCulture, $"  name: {ToonString(product.Name)}"));
        builder.AppendLine(string.Create(CultureInfo.InvariantCulture, $"  vendor: {ToonString(product.Vendor)}"));
        builder.AppendLine(string.Create(CultureInfo.InvariantCulture, $"  version: {ToonString(product.Version)}"));
        builder.AppendLine(string.Create(CultureInfo.InvariantCulture, $"  lifecycle_status: {ToonString(product.LifecycleStatus)}"));
        builder.AppendLine(string.Create(CultureInfo.InvariantCulture, $"  description: {ToonString(product.Description)}"));
        builder.AppendLine(string.Create(CultureInfo.InvariantCulture, $"  notes: {ToonString(product.Notes)}"));

        var existingMappings = product.Mappings
            .Where(x => x.TrmComponentId.HasValue && x.TrmComponent is not null)
            .OrderBy(x => x.TrmComponent!.DisplayLabel, StringComparer.OrdinalIgnoreCase)
            .ToList();

        builder.AppendLine(string.Create(CultureInfo.InvariantCulture, $"existingMappings[{existingMappings.Count}]{{component_id\tcomponent_label}}:"));
        foreach (var mapping in existingMappings)
        {
            builder.AppendLine(string.Create(CultureInfo.InvariantCulture, $"  {mapping.TrmComponentId}\t{ToonString(mapping.TrmComponent!.DisplayLabel)}"));
        }

        builder.AppendLine(string.Create(CultureInfo.InvariantCulture, $"trmComponents[{components.Count}]{{component_id\tcomponent_code\tcomponent_name\tcapability_code\tcapability_name\tdomain_code\tdomain_name\tproduct_examples\tdescription}}:"));
        foreach (var component in components)
        {
            var capability = component.ParentCapability;
            var domain = capability?.ParentDomain;
            builder.Append("  ");
            builder.Append(component.Id);
            builder.Append('\t');
            builder.Append(ToonString(component.DisplayCode));
            builder.Append('\t');
            builder.Append(ToonString(component.Name));
            builder.Append('\t');
            builder.Append(ToonString(capability?.Code));
            builder.Append('\t');
            builder.Append(ToonString(capability?.Name));
            builder.Append('\t');
            builder.Append(ToonString(domain?.Code));
            builder.Append('\t');
            builder.Append(ToonString(domain?.Name));
            builder.Append('\t');
            builder.Append(ToonString(component.ProductExamples, MaxTextCellLength));
            builder.Append('\t');
            builder.Append(ToonString(component.Description, MaxTextCellLength));
            builder.AppendLine();
        }

        return builder.ToString();
    }

    private static string ExtractAssistantContent(string responseBody)
    {
        using var document = JsonDocument.Parse(responseBody);
        if (!document.RootElement.TryGetProperty("choices", out var choices) ||
            choices.ValueKind != JsonValueKind.Array ||
            choices.GetArrayLength() == 0)
        {
            return string.Empty;
        }

        if (!choices[0].TryGetProperty("message", out var message) ||
            !message.TryGetProperty("content", out var content))
        {
            return string.Empty;
        }

        if (content.ValueKind == JsonValueKind.String)
        {
            return content.GetString() ?? string.Empty;
        }

        if (content.ValueKind != JsonValueKind.Array)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        using var enumerator = content.EnumerateArray();
        while (enumerator.MoveNext())
        {
            var item = enumerator.Current;
            if (item.ValueKind != JsonValueKind.Object ||
                !item.TryGetProperty("text", out var text) ||
                text.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            builder.Append(text.GetString());
        }

        return builder.ToString();
    }

    private static ParsedAiUsage? ParseUsage(string responseBody)
    {
        using var document = JsonDocument.Parse(responseBody);
        var root = document.RootElement;

        int? promptTokens = null;
        int? completionTokens = null;
        int? totalTokens = null;

        if (root.TryGetProperty("usage", out var usageElement) &&
            usageElement.ValueKind == JsonValueKind.Object)
        {
            promptTokens = TryReadUsageInt(usageElement, "prompt_tokens")
                ?? TryReadUsageInt(usageElement, "input_tokens");
            completionTokens = TryReadUsageInt(usageElement, "completion_tokens")
                ?? TryReadUsageInt(usageElement, "output_tokens");
            totalTokens = TryReadUsageInt(usageElement, "total_tokens");
        }

        promptTokens ??= TryReadUsageInt(root, "prompt_tokens")
            ?? TryReadUsageInt(root, "input_tokens")
            ?? TryReadUsageInt(root, "prompt_eval_count");
        completionTokens ??= TryReadUsageInt(root, "completion_tokens")
            ?? TryReadUsageInt(root, "output_tokens")
            ?? TryReadUsageInt(root, "eval_count");
        totalTokens ??= TryReadUsageInt(root, "total_tokens");
        if (!totalTokens.HasValue && (promptTokens.HasValue || completionTokens.HasValue))
        {
            totalTokens = (promptTokens ?? 0) + (completionTokens ?? 0);
        }

        if (!promptTokens.HasValue && !completionTokens.HasValue && !totalTokens.HasValue)
        {
            return null;
        }

        return new ParsedAiUsage(promptTokens, completionTokens, totalTokens);
    }

    private static int? TryReadUsageInt(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetInt32(out var integerValue) => integerValue,
            JsonValueKind.String when int.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedValue) => parsedValue,
            _ => null
        };
    }

    private static List<ParsedAiSuggestion> ParseSuggestions(string rawContent)
    {
        var normalized = StripCodeFence(rawContent);
        var lines = normalized.ReplaceLineEndings("\n").Split('\n');
        var headerIndex = Array.FindIndex(lines, line => line.TrimStart().StartsWith("suggestions[", StringComparison.OrdinalIgnoreCase));
        if (headerIndex < 0)
        {
            return [];
        }

        var suggestions = new List<ParsedAiSuggestion>();
        for (var index = headerIndex + 1; index < lines.Length; index++)
        {
            var line = lines[index];
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            if (!char.IsWhiteSpace(line[0]))
            {
                break;
            }

            var row = line.Trim();
            var parts = row.Split('\t', 3, StringSplitOptions.None);
            if (parts.Length < 3 || !int.TryParse(StripQuotes(parts[0]), NumberStyles.Integer, CultureInfo.InvariantCulture, out var componentId))
            {
                continue;
            }

            suggestions.Add(new ParsedAiSuggestion(
                componentId,
                ParseConfidence(parts[1]),
                StripQuotes(parts[2])));
        }

        return suggestions;
    }

    private static string? ExtractSummary(string rawContent)
    {
        var normalized = StripCodeFence(rawContent);
        foreach (var line in normalized.ReplaceLineEndings("\n").Split('\n'))
        {
            var trimmed = line.Trim();
            if (!trimmed.StartsWith("summary:", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return StripQuotes(trimmed["summary:".Length..].Trim());
        }

        return null;
    }

    private static decimal ParseConfidence(string value)
    {
        var trimmed = StripQuotes(value).Trim();
        if (trimmed.EndsWith('%') &&
            decimal.TryParse(trimmed[..^1], NumberStyles.Number, CultureInfo.InvariantCulture, out var percentageConfidence))
        {
            return Math.Clamp(percentageConfidence / 100m, 0m, 1m);
        }

        if (!decimal.TryParse(trimmed, NumberStyles.Number, CultureInfo.InvariantCulture, out var confidence))
        {
            return 0.5m;
        }

        if (confidence > 1m && confidence <= 100m)
        {
            confidence /= 100m;
        }

        return Math.Clamp(confidence, 0m, 1m);
    }

    private static string StripCodeFence(string value)
    {
        var trimmed = value.Trim();
        if (!trimmed.StartsWith("```", StringComparison.Ordinal) || !trimmed.EndsWith("```", StringComparison.Ordinal))
        {
            return trimmed;
        }

        var newlineIndex = trimmed.IndexOf('\n');
        if (newlineIndex < 0)
        {
            return trimmed;
        }

        return trimmed[(newlineIndex + 1)..^3].Trim();
    }

    private static string StripQuotes(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.Length >= 2 && trimmed[0] == '"' && trimmed[^1] == '"')
        {
            try
            {
                return JsonSerializer.Deserialize<string>(trimmed) ?? string.Empty;
            }
            catch (JsonException)
            {
                return trimmed[1..^1];
            }
        }

        return trimmed;
    }

    private static string ToonString(string? value, int maxLength = MaxTextCellLength)
    {
        var normalized = NormalizeTextCell(value, maxLength);
        return JsonSerializer.Serialize(normalized);
    }

    private static string NormalizeTextCell(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = value
            .Replace('\t', ' ')
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Trim();

        if (normalized.Length <= maxLength)
        {
            return normalized;
        }

        return normalized[..maxLength].TrimEnd() + "...";
    }

    private async Task<HttpResponseMessage> SendRequestAsync(
        HttpRequestMessage request,
        string operationTarget,
        int timeoutSeconds,
        CancellationToken callerCancellationToken,
        CancellationToken requestCancellationToken)
    {
        try
        {
            return await httpClient.SendAsync(request, requestCancellationToken);
        }
        catch (OperationCanceledException) when (callerCancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException exception) when (!callerCancellationToken.IsCancellationRequested && requestCancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"AI request timed out after {timeoutSeconds} second{(timeoutSeconds == 1 ? string.Empty : "s")} for {operationTarget}.",
                exception);
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException($"AI request failed for {operationTarget}: {exception.Message}", exception);
        }
    }

    private async Task WriteUsageLogAsync(
        AiProductMappingSettingsSnapshot settings,
        DateTime startedUtc,
        TimeSpan duration,
        ParsedAiUsage? usage,
        AiRequestOutcome outcome,
        string requestSummary,
        string requestKind,
        string? errorMessage,
        CancellationToken cancellationToken)
    {
        if (!settings.ActiveProviderId.HasValue || settings.ActiveProviderType is null)
        {
            return;
        }

        var truncatedErrorMessage = errorMessage;
        if (string.IsNullOrWhiteSpace(truncatedErrorMessage))
        {
            truncatedErrorMessage = null;
        }
        else if (truncatedErrorMessage.Length > 2000)
        {
            truncatedErrorMessage = truncatedErrorMessage[..2000];
        }

        dbContext.AiRequestUsageLogs.Add(new AiRequestUsageLog
        {
            AiProviderConfigurationId = settings.ActiveProviderId,
            ProviderName = settings.ActiveProviderName ?? "Unknown",
            ProviderType = settings.ActiveProviderType.Value,
            Model = settings.Model,
            RequestKind = requestKind,
            RequestSummary = requestSummary.Length <= 400 ? requestSummary : requestSummary[..400],
            PromptTokens = usage?.PromptTokens,
            CompletionTokens = usage?.CompletionTokens,
            TotalTokens = usage?.TotalTokens,
            Outcome = outcome,
            WasSuccessful = outcome == AiRequestOutcome.Success,
            DurationMilliseconds = Math.Max(0, (int)Math.Round(duration.TotalMilliseconds, MidpointRounding.AwayFromZero)),
            ErrorMessage = truncatedErrorMessage,
            OccurredUtc = startedUtc
        });

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static string GetOutcomeLabel(AiRequestOutcome outcome) =>
        outcome switch
        {
            AiRequestOutcome.Success => "Success",
            AiRequestOutcome.TimedOut => "Timed out",
            AiRequestOutcome.Cancelled => "Cancelled",
            AiRequestOutcome.Aborted => "Aborted",
            _ => "Failed"
        };

    private static string GetOutcomeBadgeClass(AiRequestOutcome outcome) =>
        outcome switch
        {
            AiRequestOutcome.Success => "text-bg-success",
            AiRequestOutcome.TimedOut => "text-bg-warning",
            AiRequestOutcome.Cancelled => "text-bg-secondary",
            AiRequestOutcome.Aborted => "text-bg-dark",
            _ => "text-bg-danger"
        };

    private static int TryParseTimeoutSeconds(string? value)
    {
        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedTimeoutSeconds))
        {
            return AppSettingDefaults.AiMappingTimeoutSeconds;
        }

        return NormalizeTimeoutSeconds(parsedTimeoutSeconds);
    }

    private static void SetCancellationOutcome(
        bool requestDispatched,
        out string errorMessage,
        out AiRequestOutcome outcome)
    {
        if (requestDispatched)
        {
            errorMessage = "AI mapping lookup was cancelled before the provider returned a response.";
            outcome = AiRequestOutcome.Cancelled;
            return;
        }

        errorMessage = "AI mapping lookup was aborted before the provider request started.";
        outcome = AiRequestOutcome.Aborted;
    }
}

public sealed class AiProductMappingSettingsSnapshot
{
    public int? ActiveProviderId { get; init; }
    public string? ActiveProviderName { get; init; }
    public AiProviderType? ActiveProviderType { get; init; }
    public string Endpoint { get; init; } = string.Empty;
    public string Model { get; init; } = string.Empty;
    public string? ApiVersion { get; init; }
    public string? ApiKey { get; init; }
    public bool IsEnabled { get; init; }
    public int TimeoutSeconds { get; init; } = AppSettingDefaults.AiMappingTimeoutSeconds;

    public bool HasActiveProvider => ActiveProviderId.HasValue;
    public bool HasSavedApiKey => !string.IsNullOrWhiteSpace(ApiKey);
    public bool IsConfigured =>
        HasActiveProvider &&
        !string.IsNullOrWhiteSpace(Endpoint) &&
        !string.IsNullOrWhiteSpace(Model) &&
        !string.IsNullOrWhiteSpace(ApiKey);
    public bool CanLookup => IsEnabled && IsConfigured;
    public string MaskedApiKey => HasSavedApiKey ? $"Stored ({ApiKey!.Length} chars)" : "Not stored";
    public string StatusLabel
    {
        get
        {
            if (!HasActiveProvider)
            {
                return "No active provider";
            }

            if (!IsConfigured)
            {
                return "Active provider incomplete";
            }

            return IsEnabled ? "Enabled" : "Disabled";
        }
    }
}

public sealed class AiProductMappingSuggestionResult
{
    public bool IsSuccess { get; init; }
    public string Message { get; init; } = string.Empty;
    public IReadOnlyList<AiProductMappingSuggestion> Suggestions { get; init; } = [];

    public static AiProductMappingSuggestionResult Success(string message, IReadOnlyList<AiProductMappingSuggestion> suggestions) =>
        new()
        {
            IsSuccess = true,
            Message = message,
            Suggestions = suggestions
        };

    public static AiProductMappingSuggestionResult Failure(string message) =>
        new()
        {
            IsSuccess = false,
            Message = message
        };
}

public sealed class AiProductMappingSuggestion
{
    public int ComponentId { get; init; }
    public string DomainLabel { get; init; } = string.Empty;
    public string CapabilityLabel { get; init; } = string.Empty;
    public string ComponentLabel { get; init; } = string.Empty;
    public decimal Confidence { get; init; }
    public string Reason { get; init; } = string.Empty;
}

public sealed class AiProviderSaveResult
{
    public bool IsSuccess { get; init; }
    public string Message { get; init; } = string.Empty;
    public int? ProviderId { get; init; }

    public static AiProviderSaveResult Success(string message, int providerId) =>
        new()
        {
            IsSuccess = true,
            Message = message,
            ProviderId = providerId
        };

    public static AiProviderSaveResult Failure(string message) =>
        new()
        {
            IsSuccess = false,
            Message = message
        };
}

public sealed class AiOperationResult
{
    public bool IsSuccess { get; init; }
    public string Message { get; init; } = string.Empty;

    public static AiOperationResult Success(string message) =>
        new()
        {
            IsSuccess = true,
            Message = message
        };

    public static AiOperationResult Failure(string message) =>
        new()
        {
            IsSuccess = false,
            Message = message
        };
}

public sealed class AiModelDiscoveryResult
{
    public bool IsSuccess { get; init; }
    public string Message { get; init; } = string.Empty;
    public IReadOnlyList<string> Models { get; init; } = [];

    public static AiModelDiscoveryResult Success(IReadOnlyList<string> models) =>
        new()
        {
            IsSuccess = true,
            Models = models
        };

    public static AiModelDiscoveryResult Failure(string message) =>
        new()
        {
            IsSuccess = false,
            Message = message
        };
}

public sealed record ParsedAiSuggestion(int ComponentId, decimal Confidence, string Reason);

public sealed record ParsedAiUsage(int? PromptTokens, int? CompletionTokens, int? TotalTokens);
