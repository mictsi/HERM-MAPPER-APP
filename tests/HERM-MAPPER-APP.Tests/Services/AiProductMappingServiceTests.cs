using HERMMapperApp.Data;
using HERMMapperApp.Models;
using HERMMapperApp.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text;
using Xunit;

namespace HERMMapperApp.Tests.Services;

public sealed class AiProductMappingServiceTests
{
    [Fact]
    public async Task SuggestMappingsAsyncBuildsToonPromptAndReturnsResolvedSuggestionsAsync()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var domain = new TrmDomain { Code = "TD001", Name = "Technology" };
        var capability = new TrmCapability { Code = "TP001", Name = "Identity", ParentDomain = domain, ParentDomainCode = domain.Code };
        var componentA = new TrmComponent { Code = "TC001", Name = "Directory Service", ParentCapability = capability, ParentCapabilityCode = capability.Code };
        var componentB = new TrmComponent { Code = "TC002", Name = "Authentication Service", ParentCapability = capability, ParentCapabilityCode = capability.Code };
        var product = new ProductCatalogItem { Name = "Active Directory", Vendor = "Microsoft", Description = "Identity directory and authentication service." };
        await fixture.DbContext.AddRangeAsync(domain, capability, componentA, componentB, product);
        await fixture.DbContext.SaveChangesAsync();
        await fixture.SeedAiSettingsAsync();
        fixture.Handler.ResponseBody = $"{{\"choices\":[{{\"message\":{{\"content\":\"summary: \\\"Suggested 2 TRM components.\\\"\\nsuggestions[2]{{component_id\\tconfidence\\treason}}:\\n  {componentA.Id}\\t0.97\\tMatches the core directory capability.\\n  {componentB.Id}\\t88%\\tSupports sign-in and authentication flows.\"}}}}]}}";

        var persistedProduct = await fixture.DbContext.ProductCatalogItems
            .Include(x => x.Mappings)
            .ThenInclude(x => x.TrmComponent)
            .SingleAsync(x => x.Id == product.Id);

        var result = await fixture.Service.SuggestMappingsAsync(persistedProduct);

        Assert.True(result.IsSuccess);
        Assert.Equal("Suggested 2 TRM components.", result.Message);
        Assert.Equal(2, result.Suggestions.Count);
        Assert.Contains("Active Directory", fixture.Handler.LastRequestBody);
        Assert.Contains("trmComponents[2]", fixture.Handler.LastRequestBody);
        Assert.Contains("existingMappings[0]", fixture.Handler.LastRequestBody);
        Assert.DoesNotContain("id:", fixture.Handler.LastRequestBody, StringComparison.Ordinal);
        Assert.DoesNotContain("version:", fixture.Handler.LastRequestBody, StringComparison.Ordinal);
        Assert.DoesNotContain("lifecycle_status:", fixture.Handler.LastRequestBody, StringComparison.Ordinal);
        Assert.DoesNotContain("description:", fixture.Handler.LastRequestBody, StringComparison.Ordinal);
        Assert.DoesNotContain("notes:", fixture.Handler.LastRequestBody, StringComparison.Ordinal);
        Assert.Equal("Bearer", fixture.Handler.LastAuthorizationScheme);
        Assert.Equal("lab-key", fixture.Handler.LastAuthorizationParameter);
        Assert.Equal(componentA.Id, result.Suggestions[0].ComponentId);
        Assert.Equal(0.88m, result.Suggestions[1].Confidence);
    }

    [Fact]
    public async Task SuggestMappingsAsyncSkipsAlreadyMappedComponentsFromAiResponseAsync()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var domain = new TrmDomain { Code = "TD001", Name = "Technology" };
        var capability = new TrmCapability { Code = "TP001", Name = "Identity", ParentDomain = domain, ParentDomainCode = domain.Code };
        var componentA = new TrmComponent { Code = "TC001", Name = "Directory Service", ParentCapability = capability, ParentCapabilityCode = capability.Code };
        var componentB = new TrmComponent { Code = "TC002", Name = "Authentication Service", ParentCapability = capability, ParentCapabilityCode = capability.Code };
        var product = new ProductCatalogItem { Name = "Active Directory" };
        var existingMapping = new ProductMapping { ProductCatalogItem = product, TrmDomain = domain, TrmCapability = capability, TrmComponent = componentA, MappingStatus = MappingStatus.Complete };
        await fixture.DbContext.AddRangeAsync(domain, capability, componentA, componentB, product, existingMapping);
        await fixture.DbContext.SaveChangesAsync();
        await fixture.SeedAiSettingsAsync();
        fixture.Handler.ResponseBody = $"{{\"choices\":[{{\"message\":{{\"content\":\"summary: \\\"Suggested mappings.\\\"\\nsuggestions[2]{{component_id\\tconfidence\\treason}}:\\n  {componentA.Id}\\t0.99\\tAlready mapped.\\n  {componentB.Id}\\t0.82\\tUseful for authentication.\"}}}}]}}";

        var persistedProduct = await fixture.DbContext.ProductCatalogItems
            .Include(x => x.Mappings)
            .ThenInclude(x => x.TrmComponent)
            .SingleAsync(x => x.Id == product.Id);

        var result = await fixture.Service.SuggestMappingsAsync(persistedProduct);

        var suggestion = Assert.Single(result.Suggestions);
        Assert.Equal(componentB.Id, suggestion.ComponentId);
    }

    [Fact]
    public async Task SuggestMappingsAsyncUsesEnabledProviderModelInRequestPayloadAsync()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var domain = new TrmDomain { Code = "TD001", Name = "Technology" };
        var capability = new TrmCapability { Code = "TP001", Name = "Identity", ParentDomain = domain, ParentDomainCode = domain.Code };
        var component = new TrmComponent { Code = "TC001", Name = "Directory Service", ParentCapability = capability, ParentCapabilityCode = capability.Code };
        var product = new ProductCatalogItem { Name = "Active Directory" };
        await fixture.DbContext.AddRangeAsync(domain, capability, component, product);

        var providerA = new AiProviderConfiguration
        {
            Name = "Open WebUI A",
            ProviderType = AiProviderType.OpenWebUi,
            Endpoint = "http://localhost:3000/api/chat/completions",
            Model = "gpt-oss:latest",
            TimeoutSeconds = 120
        };
        var providerB = new AiProviderConfiguration
        {
            Name = "Open WebUI B",
            ProviderType = AiProviderType.OpenWebUi,
            Endpoint = "http://localhost:3000/api/chat/completions",
            Model = "gpt-4.1-mini",
            TimeoutSeconds = 120,
            IsActive = true
        };

        await fixture.DbContext.AiProviderConfigurations.AddRangeAsync(providerA, providerB);
        await fixture.DbContext.AppSettings.AddAsync(new AppSetting
        {
            Key = AppSettingKeys.AiMappingIsEnabled,
            Value = "true"
        });
        await fixture.DbContext.SaveChangesAsync();
        await fixture.SetProtectedValueAsync($"AiProvider.{providerA.Id}.ApiKey", "provider-a-key");
        await fixture.SetProtectedValueAsync($"AiProvider.{providerB.Id}.ApiKey", "provider-b-key");

        fixture.Handler.ResponseBody =
            "{\"choices\":[{\"message\":{\"content\":\"summary: \\\"Suggested 1 TRM component.\\\"\\nsuggestions[1]{component_id\\tconfidence\\treason}:\\n  " +
            component.Id +
            "\\t0.95\\tMatches the core directory capability.\"}}]}";

        var persistedProduct = await fixture.DbContext.ProductCatalogItems
            .Include(x => x.Mappings)
            .ThenInclude(x => x.TrmComponent)
            .SingleAsync(x => x.Id == product.Id);

        var result = await fixture.Service.SuggestMappingsAsync(persistedProduct);

        Assert.True(result.IsSuccess);
        Assert.Contains("\"model\":\"gpt-4.1-mini\"", fixture.Handler.LastRequestBody);
        Assert.DoesNotContain("\"model\":\"gpt-oss:latest\"", fixture.Handler.LastRequestBody);
        Assert.Equal("provider-b-key", fixture.Handler.LastAuthorizationParameter);
    }

    [Fact]
    public async Task SuggestMappingsAsyncRetriesAzureAiFoundryRequestsAgainstResponsesApiAsync()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var domain = new TrmDomain { Code = "TD001", Name = "Technology" };
        var capability = new TrmCapability { Code = "TP001", Name = "Identity", ParentDomain = domain, ParentDomainCode = domain.Code };
        var component = new TrmComponent { Code = "TC001", Name = "Directory Service", ParentCapability = capability, ParentCapabilityCode = capability.Code };
        var product = new ProductCatalogItem { Name = "Active Directory", Description = "Identity directory and authentication service." };
        await fixture.DbContext.AddRangeAsync(domain, capability, component, product);

        var provider = new AiProviderConfiguration
        {
            Name = "Azure AI Foundry",
            ProviderType = AiProviderType.AzureAiFoundry,
            Endpoint = "https://ai-lab-foundry-local.openai.azure.com/openai/v1/chat/completions",
            Model = "gpt-5.4-nano",
            TimeoutSeconds = 120,
            IsActive = true
        };

        await fixture.DbContext.AiProviderConfigurations.AddAsync(provider);
        await fixture.DbContext.AppSettings.AddAsync(new AppSetting
        {
            Key = AppSettingKeys.AiMappingIsEnabled,
            Value = "true"
        });
        await fixture.DbContext.SaveChangesAsync();
        await fixture.SetProtectedValueAsync($"AiProvider.{provider.Id}.ApiKey", "foundry-key");

        fixture.Handler.EnqueueResponse(
            "/openai/v1/chat/completions",
            HttpStatusCode.BadRequest,
            """
            {"error":{"message":"Unsupported parameter: 'messages'. In the Responses API, this parameter has moved to 'input'. Try again with the new parameter.","type":"invalid_request_error"}}
            """);
        fixture.Handler.EnqueueResponse(
            "/openai/v1/responses",
            HttpStatusCode.OK,
            "{" +
            "\"output\":[{\"type\":\"message\",\"content\":[{\"type\":\"output_text\",\"text\":\"summary: \\\"Suggested 1 TRM component.\\\"\\nsuggestions[1]{component_id\\tconfidence\\treason}:\\n  " +
            component.Id +
            "\\t0.91\\tMatches the core directory capability.\"}]}]," +
            "\"usage\":{\"input_tokens\":81,\"output_tokens\":19,\"total_tokens\":100}}"
        );

        var persistedProduct = await fixture.DbContext.ProductCatalogItems
            .Include(x => x.Mappings)
            .ThenInclude(x => x.TrmComponent)
            .SingleAsync(x => x.Id == product.Id);

        var result = await fixture.Service.SuggestMappingsAsync(persistedProduct);

        Assert.True(result.IsSuccess);
        Assert.Equal("Suggested 1 TRM component.", result.Message);
        var suggestion = Assert.Single(result.Suggestions);
        Assert.Equal(component.Id, suggestion.ComponentId);
        Assert.Equal(0.91m, suggestion.Confidence);
        Assert.Equal(["/openai/v1/chat/completions", "/openai/v1/responses"], fixture.Handler.RequestPaths);
        Assert.Contains("\"input\"", fixture.Handler.LastRequestBody);
        Assert.DoesNotContain("\"messages\"", fixture.Handler.LastRequestBody);
        Assert.Contains("\"instructions\"", fixture.Handler.LastRequestBody);
        Assert.Contains("\"input\":\"request:", fixture.Handler.LastRequestBody);
        Assert.Equal("Bearer", fixture.Handler.LastAuthorizationScheme);
        Assert.Equal("foundry-key", fixture.Handler.LastAuthorizationParameter);
        Assert.Null(fixture.Handler.LastApiKeyHeader);

        var usageLog = await fixture.DbContext.AiRequestUsageLogs.SingleAsync();
        Assert.Equal(81, usageLog.PromptTokens);
        Assert.Equal(19, usageLog.CompletionTokens);
        Assert.Equal(100, usageLog.TotalTokens);
        Assert.Equal(AiRequestOutcome.Success, usageLog.Outcome);
    }

    [Fact]
    public async Task GetSettingsAsyncReturnsSavedTimeoutSecondsAsync()
    {
        await using var fixture = await TestFixture.CreateAsync();
        await fixture.SeedAiSettingsAsync(timeoutSeconds: 180);

        var settings = await fixture.Service.GetSettingsAsync();

        Assert.True(settings.HasActiveProvider);
        Assert.Equal(180, settings.TimeoutSeconds);
    }

    [Fact]
    public async Task SuggestMappingsAsyncReturnsFailureWhenLookupTimesOutAsync()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var domain = new TrmDomain { Code = "TD001", Name = "Technology" };
        var capability = new TrmCapability { Code = "TP001", Name = "Identity", ParentDomain = domain, ParentDomainCode = domain.Code };
        var component = new TrmComponent { Code = "TC001", Name = "Directory Service", ParentCapability = capability, ParentCapabilityCode = capability.Code };
        var product = new ProductCatalogItem { Name = "Active Directory" };
        await fixture.DbContext.AddRangeAsync(domain, capability, component, product);
        await fixture.DbContext.SaveChangesAsync();
        await fixture.SeedAiSettingsAsync(timeoutSeconds: 1);
        fixture.Handler.BlockUntilCanceled = true;

        var persistedProduct = await fixture.DbContext.ProductCatalogItems
            .Include(x => x.Mappings)
            .ThenInclude(x => x.TrmComponent)
            .SingleAsync(x => x.Id == product.Id);

        var result = await fixture.Service.SuggestMappingsAsync(persistedProduct);

        Assert.False(result.IsSuccess);
        Assert.Equal("AI mapping lookup timed out after 1 second.", result.Message);

        var usageLog = await fixture.DbContext.AiRequestUsageLogs.SingleAsync();
        Assert.Equal(AiRequestOutcome.TimedOut, usageLog.Outcome);
        Assert.False(usageLog.WasSuccessful);
        Assert.Contains("timed out", usageLog.ErrorMessage ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetAvailableModelsAsyncReturnsOpenWebUiModelsAsync()
    {
        await using var fixture = await TestFixture.CreateAsync();
        await fixture.SeedAiSettingsAsync();

        var settings = await fixture.Service.GetSettingsAsync();
        fixture.Handler.ResponseByPath["/api/models"] = "{\"data\":[{\"id\":\"gpt-oss:latest\"},{\"id\":\"llama3.3:70b\"}]}";

        var result = await fixture.Service.GetAvailableModelsAsync(settings.ActiveProviderId!.Value);

        Assert.True(result.IsSuccess);
        Assert.Equal(["gpt-oss:latest", "llama3.3:70b"], result.Models);
        Assert.Equal("/api/models", fixture.Handler.LastRequestPath);
        Assert.Equal("Bearer", fixture.Handler.LastAuthorizationScheme);
        Assert.Equal("lab-key", fixture.Handler.LastAuthorizationParameter);
    }

    [Fact]
    public async Task GetAvailableModelsAsyncReturnsOpenWebUiModelsWhenModelIsBlankAsync()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var provider = new AiProviderConfiguration
        {
            Name = "Open WebUI",
            ProviderType = AiProviderType.OpenWebUi,
            Endpoint = "http://localhost:3000/api/chat/completions",
            Model = string.Empty,
            TimeoutSeconds = 120,
            IsActive = true
        };

        await fixture.DbContext.AiProviderConfigurations.AddAsync(provider);
        await fixture.DbContext.AppSettings.AddAsync(new AppSetting
        {
            Key = AppSettingKeys.AiMappingIsEnabled,
            Value = "true"
        });
        await fixture.DbContext.SaveChangesAsync();
        await fixture.SetProtectedValueAsync($"AiProvider.{provider.Id}.ApiKey", "lab-key");

        fixture.Handler.ResponseByPath["/api/models"] = "{\"data\":[{\"id\":\"gpt-oss:latest\"},{\"id\":\"llama3.3:70b\"}]}";

        var result = await fixture.Service.GetAvailableModelsAsync(provider.Id);

        Assert.True(result.IsSuccess);
        Assert.Equal(["gpt-oss:latest", "llama3.3:70b"], result.Models);
        Assert.Equal("/api/models", fixture.Handler.LastRequestPath);
    }

    [Fact]
    public async Task SuggestMappingsAsyncWritesUsageLogFromUsagePayloadAsync()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var domain = new TrmDomain { Code = "TD001", Name = "Technology" };
        var capability = new TrmCapability { Code = "TP001", Name = "Identity", ParentDomain = domain, ParentDomainCode = domain.Code };
        var component = new TrmComponent { Code = "TC001", Name = "Directory Service", ParentCapability = capability, ParentCapabilityCode = capability.Code };
        var product = new ProductCatalogItem { Name = "Active Directory" };
        await fixture.DbContext.AddRangeAsync(domain, capability, component, product);
        await fixture.DbContext.SaveChangesAsync();
        await fixture.SeedAiSettingsAsync();
        await fixture.Service.GetSettingsAsync();
        var provider = await fixture.DbContext.AiProviderConfigurations.SingleAsync();
        provider.InputCostPerMillionTokensSek = 10m;
        provider.OutputCostPerMillionTokensSek = 20m;
        await fixture.DbContext.SaveChangesAsync();
        fixture.Handler.ResponseBody =
            "{\"choices\":[{\"message\":{\"content\":\"summary: \\\"Suggested 1 TRM component.\\\"\\nsuggestions[1]{component_id\\tconfidence\\treason}:\\n  " +
            component.Id +
            "\\t0.95\\tMatches core identity and directory capabilities.\"}}],\"usage\":{\"prompt_tokens\":120,\"completion_tokens\":34,\"total_tokens\":154}}";

        var persistedProduct = await fixture.DbContext.ProductCatalogItems
            .Include(x => x.Mappings)
            .ThenInclude(x => x.TrmComponent)
            .SingleAsync(x => x.Id == product.Id);

        var result = await fixture.Service.SuggestMappingsAsync(persistedProduct);

        Assert.True(result.IsSuccess);

        var usageLog = await fixture.DbContext.AiRequestUsageLogs.SingleAsync();
        Assert.Equal("Open WebUI (Migrated)", usageLog.ProviderName);
        Assert.Equal("gpt-oss:latest", usageLog.Model);
        Assert.Equal(120, usageLog.PromptTokens);
        Assert.Equal(34, usageLog.CompletionTokens);
        Assert.Equal(154, usageLog.TotalTokens);
        Assert.Equal(0.0012m, usageLog.EstimatedInputCostSek);
        Assert.Equal(0.00068m, usageLog.EstimatedOutputCostSek);
        Assert.Equal(0.00188m, usageLog.EstimatedTotalCostSek);
        Assert.Equal(AiRequestOutcome.Success, usageLog.Outcome);
        Assert.True(usageLog.WasSuccessful);
        Assert.Contains("Active Directory", usageLog.RequestSummary);
    }

    [Fact]
    public async Task SuggestMappingsAsyncWritesUsageLogFromOpenWebUiUsageFieldsAsync()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var domain = new TrmDomain { Code = "TD001", Name = "Technology" };
        var capability = new TrmCapability { Code = "TP001", Name = "Identity", ParentDomain = domain, ParentDomainCode = domain.Code };
        var component = new TrmComponent { Code = "TC001", Name = "Directory Service", ParentCapability = capability, ParentCapabilityCode = capability.Code };
        var product = new ProductCatalogItem { Name = "Active Directory" };
        await fixture.DbContext.AddRangeAsync(domain, capability, component, product);
        await fixture.DbContext.SaveChangesAsync();
        await fixture.SeedAiSettingsAsync();
        fixture.Handler.ResponseBody =
            "{\"choices\":[{\"message\":{\"content\":\"summary: \\\"Suggested 1 TRM component.\\\"\\nsuggestions[1]{component_id\\tconfidence\\treason}:\\n  " +
            component.Id +
            "\\t0.93\\tMatches directory and identity responsibilities.\"}}],\"prompt_eval_count\":81,\"eval_count\":19}";

        var persistedProduct = await fixture.DbContext.ProductCatalogItems
            .Include(x => x.Mappings)
            .ThenInclude(x => x.TrmComponent)
            .SingleAsync(x => x.Id == product.Id);

        var result = await fixture.Service.SuggestMappingsAsync(persistedProduct);

        Assert.True(result.IsSuccess);

        var usageLog = await fixture.DbContext.AiRequestUsageLogs.SingleAsync();
        Assert.Equal(81, usageLog.PromptTokens);
        Assert.Equal(19, usageLog.CompletionTokens);
        Assert.Equal(100, usageLog.TotalTokens);
        Assert.Equal(AiRequestOutcome.Success, usageLog.Outcome);
    }

    [Fact]
    public async Task BuildAdminViewModelAsyncDoesNotReorderProvidersByActiveStateAsync()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var betaProvider = new AiProviderConfiguration
        {
            Name = "Beta Provider",
            ProviderType = AiProviderType.OpenWebUi,
            Endpoint = "http://localhost:3000/api/chat/completions",
            Model = "gpt-oss:latest",
            TimeoutSeconds = 120,
            IsActive = true
        };
        var alphaProvider = new AiProviderConfiguration
        {
            Name = "Alpha Provider",
            ProviderType = AiProviderType.OpenAiApi,
            Endpoint = "https://api.openai.com/v1/chat/completions",
            Model = "gpt-4.1",
            TimeoutSeconds = 120
        };

        await fixture.DbContext.AiProviderConfigurations.AddRangeAsync(betaProvider, alphaProvider);
        await fixture.DbContext.SaveChangesAsync();

        var model = await fixture.Service.BuildAdminViewModelAsync();

        Assert.Equal(["Alpha Provider", "Beta Provider"], model.Providers.Select(x => x.Name).ToArray());
        Assert.False(model.Providers[0].IsActive);
        Assert.True(model.Providers[1].IsActive);
    }

    [Fact]
    public async Task SuggestMappingsAsyncWritesUsageLogForCancelledRequestsAsync()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var domain = new TrmDomain { Code = "TD001", Name = "Technology" };
        var capability = new TrmCapability { Code = "TP001", Name = "Identity", ParentDomain = domain, ParentDomainCode = domain.Code };
        var component = new TrmComponent { Code = "TC001", Name = "Directory Service", ParentCapability = capability, ParentCapabilityCode = capability.Code };
        var product = new ProductCatalogItem { Name = "Active Directory" };
        await fixture.DbContext.AddRangeAsync(domain, capability, component, product);
        await fixture.DbContext.SaveChangesAsync();
        await fixture.SeedAiSettingsAsync(timeoutSeconds: 120);
        fixture.Handler.BlockUntilCanceled = true;

        var persistedProduct = await fixture.DbContext.ProductCatalogItems
            .Include(x => x.Mappings)
            .ThenInclude(x => x.TrmComponent)
            .SingleAsync(x => x.Id == product.Id);

        using var cancellationTokenSource = new CancellationTokenSource();
        cancellationTokenSource.CancelAfter(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await fixture.Service.SuggestMappingsAsync(persistedProduct, cancellationTokenSource.Token));

        var usageLog = await fixture.DbContext.AiRequestUsageLogs.SingleAsync();
        Assert.Equal(AiRequestOutcome.Cancelled, usageLog.Outcome);
        Assert.False(usageLog.WasSuccessful);
        Assert.Contains("cancelled", usageLog.ErrorMessage ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SuggestMappingsAsyncWritesUsageLogForAbortedRequestsAsync()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var domain = new TrmDomain { Code = "TD001", Name = "Technology" };
        var capability = new TrmCapability { Code = "TP001", Name = "Identity", ParentDomain = domain, ParentDomainCode = domain.Code };
        var component = new TrmComponent { Code = "TC001", Name = "Directory Service", ParentCapability = capability, ParentCapabilityCode = capability.Code };
        var product = new ProductCatalogItem { Name = "Active Directory" };
        await fixture.DbContext.AddRangeAsync(domain, capability, component, product);
        await fixture.DbContext.SaveChangesAsync();
        await fixture.SeedAiSettingsAsync();
        fixture.Handler.ExceptionToThrow = new HttpRequestException("Connection reset by peer.");

        var persistedProduct = await fixture.DbContext.ProductCatalogItems
            .Include(x => x.Mappings)
            .ThenInclude(x => x.TrmComponent)
            .SingleAsync(x => x.Id == product.Id);

        var result = await fixture.Service.SuggestMappingsAsync(persistedProduct);

        Assert.False(result.IsSuccess);
        Assert.Contains("Connection reset by peer.", result.Message);

        var usageLog = await fixture.DbContext.AiRequestUsageLogs.SingleAsync();
        Assert.Equal(AiRequestOutcome.Aborted, usageLog.Outcome);
        Assert.False(usageLog.WasSuccessful);
        Assert.Contains("Connection reset by peer.", usageLog.ErrorMessage ?? string.Empty);
    }

    [Fact]
    public async Task BuildAdminViewModelAsyncAggregatesOutcomeStatsAsync()
    {
        await using var fixture = await TestFixture.CreateAsync();
        await fixture.SeedAiSettingsAsync();
        var settings = await fixture.Service.GetSettingsAsync();

        var today = DateTime.UtcNow.Date.AddHours(8);
        var yesterday = today.AddDays(-1);
        var sixDaysAgo = today.AddDays(-6);

        await fixture.DbContext.AiRequestUsageLogs.AddRangeAsync(
            new AiRequestUsageLog
            {
                AiProviderConfigurationId = settings.ActiveProviderId,
                ProviderName = settings.ActiveProviderName ?? "Open WebUI",
                ProviderType = settings.ActiveProviderType!.Value,
                Model = settings.Model,
                RequestKind = "SuggestProductTrmMappings",
                RequestSummary = "SuggestProductTrmMappings: Active Directory",
                PromptTokens = 80,
                CompletionTokens = 20,
                TotalTokens = 100,
                EstimatedInputCostSek = 0.0008m,
                EstimatedOutputCostSek = 0.0004m,
                EstimatedTotalCostSek = 0.0012m,
                Outcome = AiRequestOutcome.Success,
                WasSuccessful = true,
                DurationMilliseconds = 1500,
                OccurredUtc = today
            },
            new AiRequestUsageLog
            {
                AiProviderConfigurationId = settings.ActiveProviderId,
                ProviderName = settings.ActiveProviderName ?? "Open WebUI",
                ProviderType = settings.ActiveProviderType!.Value,
                Model = settings.Model,
                RequestKind = "SuggestProductTrmMappings",
                RequestSummary = "SuggestProductTrmMappings: Exchange",
                Outcome = AiRequestOutcome.TimedOut,
                WasSuccessful = false,
                DurationMilliseconds = 120000,
                ErrorMessage = "AI mapping lookup timed out after 120 seconds.",
                OccurredUtc = today
            },
            new AiRequestUsageLog
            {
                AiProviderConfigurationId = settings.ActiveProviderId,
                ProviderName = settings.ActiveProviderName ?? "Open WebUI",
                ProviderType = settings.ActiveProviderType!.Value,
                Model = settings.Model,
                RequestKind = "SuggestProductTrmMappings",
                RequestSummary = "SuggestProductTrmMappings: SharePoint",
                Outcome = AiRequestOutcome.Cancelled,
                WasSuccessful = false,
                DurationMilliseconds = 5000,
                ErrorMessage = "AI mapping lookup was cancelled before the provider returned a response.",
                OccurredUtc = yesterday
            },
            new AiRequestUsageLog
            {
                AiProviderConfigurationId = settings.ActiveProviderId,
                ProviderName = settings.ActiveProviderName ?? "Open WebUI",
                ProviderType = settings.ActiveProviderType!.Value,
                Model = settings.Model,
                RequestKind = "SuggestProductTrmMappings",
                RequestSummary = "SuggestProductTrmMappings: Teams",
                Outcome = AiRequestOutcome.Aborted,
                WasSuccessful = false,
                DurationMilliseconds = 1000,
                ErrorMessage = "AI request failed for mapping lookup for product 'Teams': Connection reset.",
                OccurredUtc = sixDaysAgo
            });
        await fixture.DbContext.SaveChangesAsync();

        var model = await fixture.Service.BuildAdminViewModelAsync();

        Assert.Equal(2, model.Dashboard.RequestsToday);
        Assert.Equal(4, model.Dashboard.RequestsLast7Days);
        Assert.Equal(100, model.Dashboard.TokensToday);
        Assert.Equal(100, model.Dashboard.TokensLast7Days);
        Assert.Equal(100, model.Dashboard.AverageTokensPerRequestLast7Days);
        Assert.Equal(0.0012m, model.Dashboard.CostTodaySek);
        Assert.Equal(0.0012m, model.Dashboard.CostLast7DaysSek);
        Assert.Equal(1, model.Dashboard.TimedOutRequestsToday);
        Assert.Equal(1, model.Dashboard.TimedOutRequestsLast7Days);
        Assert.Equal(0, model.Dashboard.CancelledRequestsToday);
        Assert.Equal(1, model.Dashboard.CancelledRequestsLast7Days);
        Assert.Equal(0, model.Dashboard.AbortedRequestsToday);
        Assert.Equal(1, model.Dashboard.AbortedRequestsLast7Days);
        Assert.Equal(0.0012m, Assert.Single(model.Providers).TotalCostLast7DaysSek);
        Assert.Contains(model.RecentUsage, entry => entry.Outcome == AiRequestOutcome.TimedOut && entry.OutcomeLabel == "Timed out");
        Assert.Contains(model.RecentUsage, entry => entry.Outcome == AiRequestOutcome.Cancelled && entry.OutcomeLabel == "Cancelled");
        Assert.Contains(model.RecentUsage, entry => entry.Outcome == AiRequestOutcome.Aborted && entry.OutcomeLabel == "Aborted");
    }

    [Fact]
    public async Task BuildAdminViewModelAsyncAggregatesEstimatedCostWindowsAsync()
    {
        await using var fixture = await TestFixture.CreateAsync();
        await fixture.SeedAiSettingsAsync();
        var settings = await fixture.Service.GetSettingsAsync();

        var today = DateTime.UtcNow.Date.AddHours(9);
        var tenDaysAgo = today.AddDays(-10);
        var twoHundredDaysAgo = today.AddDays(-200);
        var fourHundredDaysAgo = today.AddDays(-400);

        await fixture.DbContext.AiRequestUsageLogs.AddRangeAsync(
            new AiRequestUsageLog
            {
                AiProviderConfigurationId = settings.ActiveProviderId,
                ProviderName = settings.ActiveProviderName ?? "Open WebUI",
                ProviderType = settings.ActiveProviderType!.Value,
                Model = settings.Model,
                RequestKind = "SuggestProductTrmMappings",
                RequestSummary = "SuggestProductTrmMappings: Active Directory",
                PromptTokens = 100,
                CompletionTokens = 25,
                TotalTokens = 125,
                EstimatedInputCostSek = 0.10m,
                EstimatedOutputCostSek = 0.05m,
                EstimatedTotalCostSek = 0.15m,
                Outcome = AiRequestOutcome.Success,
                WasSuccessful = true,
                DurationMilliseconds = 800,
                OccurredUtc = today
            },
            new AiRequestUsageLog
            {
                AiProviderConfigurationId = settings.ActiveProviderId,
                ProviderName = settings.ActiveProviderName ?? "Open WebUI",
                ProviderType = settings.ActiveProviderType!.Value,
                Model = settings.Model,
                RequestKind = "SuggestProductTrmMappings",
                RequestSummary = "SuggestProductTrmMappings: Exchange",
                PromptTokens = 80,
                CompletionTokens = 20,
                TotalTokens = 100,
                EstimatedInputCostSek = 0.12m,
                EstimatedOutputCostSek = 0.08m,
                EstimatedTotalCostSek = 0.20m,
                Outcome = AiRequestOutcome.Success,
                WasSuccessful = true,
                DurationMilliseconds = 900,
                OccurredUtc = tenDaysAgo
            },
            new AiRequestUsageLog
            {
                AiProviderConfigurationId = settings.ActiveProviderId,
                ProviderName = settings.ActiveProviderName ?? "Open WebUI",
                ProviderType = settings.ActiveProviderType!.Value,
                Model = settings.Model,
                RequestKind = "SuggestProductTrmMappings",
                RequestSummary = "SuggestProductTrmMappings: Teams",
                PromptTokens = 60,
                CompletionTokens = 15,
                TotalTokens = 75,
                EstimatedInputCostSek = 0.18m,
                EstimatedOutputCostSek = 0.12m,
                EstimatedTotalCostSek = 0.30m,
                Outcome = AiRequestOutcome.Success,
                WasSuccessful = true,
                DurationMilliseconds = 700,
                OccurredUtc = twoHundredDaysAgo
            },
            new AiRequestUsageLog
            {
                AiProviderConfigurationId = settings.ActiveProviderId,
                ProviderName = settings.ActiveProviderName ?? "Open WebUI",
                ProviderType = settings.ActiveProviderType!.Value,
                Model = settings.Model,
                RequestKind = "SuggestProductTrmMappings",
                RequestSummary = "SuggestProductTrmMappings: Legacy",
                PromptTokens = 40,
                CompletionTokens = 10,
                TotalTokens = 50,
                EstimatedInputCostSek = 0.24m,
                EstimatedOutputCostSek = 0.16m,
                EstimatedTotalCostSek = 0.40m,
                Outcome = AiRequestOutcome.Success,
                WasSuccessful = true,
                DurationMilliseconds = 600,
                OccurredUtc = fourHundredDaysAgo
            });
        await fixture.DbContext.SaveChangesAsync();

        var model = await fixture.Service.BuildAdminViewModelAsync();

        Assert.Equal(0.15m, model.Dashboard.CostTodaySek);
        Assert.Equal(0.15m, model.Dashboard.CostLast7DaysSek);
        Assert.Equal(0.35m, model.Dashboard.CostLast30DaysSek);
        Assert.Equal(0.65m, model.Dashboard.CostLast365DaysSek);

        var latestUsage = model.RecentUsage[0];
        Assert.Equal(0.10m, latestUsage.EstimatedInputCostSek);
        Assert.Equal(0.05m, latestUsage.EstimatedOutputCostSek);
        Assert.Equal(0.15m, latestUsage.EstimatedTotalCostSek);
    }

    private sealed class TestFixture : IAsyncDisposable
    {
        private readonly SqliteConnection connection;
        private readonly HttpClient httpClient;

        private TestFixture(
            SqliteConnection connection,
            AppDbContext dbContext,
            CapturingHttpMessageHandler handler,
            HttpClient httpClient,
            AiProductMappingService service,
            ProtectedSettingsService protectedSettingsService)
        {
            this.connection = connection;
            this.httpClient = httpClient;
            DbContext = dbContext;
            Handler = handler;
            Service = service;
            ProtectedSettings = protectedSettingsService;
        }

        public AppDbContext DbContext { get; }
        public CapturingHttpMessageHandler Handler { get; }
        public AiProductMappingService Service { get; }
        public ProtectedSettingsService ProtectedSettings { get; }

        public static async Task<TestFixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();

            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(connection)
                .Options;

            var dbContext = new AppDbContext(options);
            await dbContext.Database.EnsureCreatedAsync();

            var appSettingsService = new AppSettingsService(dbContext);
            var protectedSettingsService = new ProtectedSettingsService(
                new EphemeralDataProtectionProvider(),
                appSettingsService,
                NullLogger<ProtectedSettingsService>.Instance);
            var handler = new CapturingHttpMessageHandler();
            var httpClient = new HttpClient(handler);
            var service = new AiProductMappingService(
                dbContext,
                appSettingsService,
                protectedSettingsService,
                new AuditLogService(dbContext),
                httpClient,
                NullLogger<AiProductMappingService>.Instance);

            return new TestFixture(connection, dbContext, handler, httpClient, service, protectedSettingsService);
        }

        public async Task SeedAiSettingsAsync(int timeoutSeconds = AppSettingDefaults.AiMappingTimeoutSeconds)
        {
            await DbContext.AppSettings.AddRangeAsync(
                new AppSetting { Key = AppSettingKeys.AiMappingEndpoint, Value = "http://localhost:3000/api/chat/completions" },
                new AppSetting { Key = AppSettingKeys.AiMappingModel, Value = "gpt-oss:latest" },
                new AppSetting { Key = AppSettingKeys.AiMappingApiKey, Value = "lab-key" },
                new AppSetting { Key = AppSettingKeys.AiMappingIsEnabled, Value = "true" },
                new AppSetting { Key = AppSettingKeys.AiMappingTimeoutSeconds, Value = timeoutSeconds.ToString(CultureInfo.InvariantCulture) });
            await DbContext.SaveChangesAsync();
        }

        public Task SetProtectedValueAsync(string key, string value)
            => ProtectedSettings.SetValueAsync(key, value);

        public async ValueTask DisposeAsync()
        {
            httpClient.Dispose();
            Handler.Dispose();
            await DbContext.DisposeAsync();
            await connection.DisposeAsync();
        }
    }

    public sealed class CapturingHttpMessageHandler : HttpMessageHandler
    {
        private sealed record QueuedResponse(HttpStatusCode StatusCode, string ResponseBody);

        public string ResponseBody { get; set; } =
            "{\"choices\":[{\"message\":{\"content\":\"summary: \\\"No suggestions\\\"\\nsuggestions[0]{component_id\\tconfidence\\treason}:\"}}]}";

        public Dictionary<string, string> ResponseByPath { get; } = new(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, Queue<QueuedResponse>> QueuedResponsesByPath { get; } = new(StringComparer.OrdinalIgnoreCase);
        public bool BlockUntilCanceled { get; set; }
        public Exception? ExceptionToThrow { get; set; }
        public string LastRequestBody { get; private set; } = string.Empty;
        public string? LastAuthorizationScheme { get; private set; }
        public string? LastAuthorizationParameter { get; private set; }
        public string? LastApiKeyHeader { get; private set; }
        public string LastRequestPath { get; private set; } = string.Empty;
        public List<string> RequestPaths { get; } = [];

        public void EnqueueResponse(string path, HttpStatusCode statusCode, string responseBody)
        {
            if (!QueuedResponsesByPath.TryGetValue(path, out var responses))
            {
                responses = new Queue<QueuedResponse>();
                QueuedResponsesByPath[path] = responses;
            }

            responses.Enqueue(new QueuedResponse(statusCode, responseBody));
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequestBody = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            LastAuthorizationScheme = request.Headers.Authorization?.Scheme;
            LastAuthorizationParameter = request.Headers.Authorization?.Parameter;
            LastApiKeyHeader = request.Headers.TryGetValues("api-key", out var apiKeyValues)
                ? apiKeyValues.SingleOrDefault()
                : null;
            LastRequestPath = request.RequestUri?.AbsolutePath ?? string.Empty;
            RequestPaths.Add(LastRequestPath);

            if (BlockUntilCanceled)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }

            if (ExceptionToThrow is not null)
            {
                throw ExceptionToThrow;
            }

            HttpStatusCode statusCode;
            string responseBody;

            if (QueuedResponsesByPath.TryGetValue(LastRequestPath, out var queuedResponses) && queuedResponses.Count > 0)
            {
                var queuedResponse = queuedResponses.Dequeue();
                statusCode = queuedResponse.StatusCode;
                responseBody = queuedResponse.ResponseBody;
            }
            else
            {
                statusCode = HttpStatusCode.OK;
                responseBody = ResponseByPath.TryGetValue(LastRequestPath, out var mappedResponseBody)
                    ? mappedResponseBody
                    : ResponseBody;
            }

            return new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json")
            };
        }
    }
}
