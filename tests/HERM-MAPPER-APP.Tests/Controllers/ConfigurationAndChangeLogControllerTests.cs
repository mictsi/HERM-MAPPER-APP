using HERMMapperApp.Controllers;
using HERMMapperApp.Data;
using HERMMapperApp.Models;
using HERMMapperApp.Services;
using HERMMapperApp.Tests.TestSupport;
using HERMMapperApp.ViewModels;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using System.Net;
using System.Net.Http;
using System.Text;
using Xunit;

namespace HERMMapperApp.Tests.Controllers;

public sealed class ConfigurationAndChangeLogControllerTests
{
    [Fact]
    public async Task ChangeLogIndexFiltersBySearchAndOrdersNewestFirstAsync()
    {
        await using var fixture = await TestFixture.CreateAsync();
        await fixture.DbContext.AuditLogEntries.AddRangeAsync(
            new AuditLogEntry
            {
                ActorUserName = "import.service",
                Category = "Configuration",
                Action = "Import",
                EntityType = "TrmWorkbook",
                Summary = "Imported workbook",
                Details = "Workbook import passed",
                OccurredUtc = new DateTime(2026, 3, 1, 9, 0, 0, DateTimeKind.Utc)
            },
            new AuditLogEntry
            {
                ActorUserName = "ada",
                Category = "Product",
                Action = "Create",
                EntityType = nameof(ProductCatalogItem),
                Summary = "Created Sentinel",
                OccurredUtc = new DateTime(2026, 3, 3, 9, 0, 0, DateTimeKind.Utc)
            },
            new AuditLogEntry
            {
                ActorUserName = "sam",
                Category = "Configuration",
                Action = "VerifyProductImport",
                EntityType = nameof(ProductCatalogItem),
                Summary = "Verified CSV",
                Details = "Rows read: 1",
                OccurredUtc = new DateTime(2026, 3, 4, 9, 0, 0, DateTimeKind.Utc)
            });
        await fixture.DbContext.SaveChangesAsync();

        using var controller = fixture.CreateChangeLogController();
        var result = await controller.IndexAsync("Product");

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<ChangeLogIndexViewModel>(view.Model);

        Assert.Equal("Product", model.Search);
        Assert.Equal(2, model.Entries.Count);
        Assert.Equal("VerifyProductImport", model.Entries[0].Action);
        Assert.Equal("Create", model.Entries[1].Action);
    }

    [Fact]
    public async Task ChangeLogIndexFiltersByActorUserNameAsync()
    {
        await using var fixture = await TestFixture.CreateAsync();
        await fixture.DbContext.AuditLogEntries.AddRangeAsync(
            new AuditLogEntry
            {
                ActorUserName = "ada",
                Category = "Product",
                Action = "Create",
                Summary = "Created Sentinel",
                OccurredUtc = new DateTime(2026, 3, 3, 9, 0, 0, DateTimeKind.Utc)
            },
            new AuditLogEntry
            {
                ActorUserName = "sam",
                Category = "Configuration",
                Action = "Import",
                Summary = "Imported workbook",
                OccurredUtc = new DateTime(2026, 3, 4, 9, 0, 0, DateTimeKind.Utc)
            });
        await fixture.DbContext.SaveChangesAsync();

        using var controller = fixture.CreateChangeLogController();
        var result = await controller.IndexAsync("ada");

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<ChangeLogIndexViewModel>(view.Model);

        var entry = Assert.Single(model.Entries);
        Assert.Equal("ada", entry.ActorUserName);
        Assert.Equal("Create", entry.Action);
    }

    [Fact]
    public async Task AddOptionCreatesOptionAndWritesAuditLogAsync()
    {
        await using var fixture = await TestFixture.CreateAsync();
        using var controller = fixture.CreateConfigurationController();

        var result = await controller.AddOptionAsync(new AddConfigurationOptionInputModel
        {
            FieldName = " Owner ",
            Value = " Team Blue "
        });

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Index", redirect.ActionName);
        Assert.Equal(ConfigurableFieldNames.Owner, Assert.IsType<string>(redirect.RouteValues!["expandedFieldName"]));

        var option = await fixture.DbContext.ConfigurableFieldOptions.SingleAsync();
        var audit = await fixture.DbContext.AuditLogEntries.SingleAsync();

        Assert.Equal(ConfigurableFieldNames.Owner, option.FieldName);
        Assert.Equal("Team Blue", option.Value);
        Assert.Equal(1, option.SortOrder);
        Assert.Equal("Owner value 'Team Blue' was added.", controller.TempData["ConfigurationStatusMessage"]);
        Assert.Equal("Create", audit.Action);
        Assert.Equal(nameof(ConfigurableFieldOption), audit.EntityType);
    }

    [Fact]
    public async Task IndexBuildsViewModelFromSettingsAndTempDataAsync()
    {
        await using var fixture = await TestFixture.CreateAsync();
        fixture.DbContext.AppSettings.Add(new AppSetting
        {
            Key = AppSettingKeys.DisplayTimeZone,
            Value = "UTC",
            UpdatedUtc = DateTime.UtcNow
        });
        await fixture.DbContext.SaveChangesAsync();

        using var controller = fixture.CreateConfigurationController();
        controller.TempData["ConfigurationStatusMessage"] = "Saved";
        controller.TempData["ConfigurationError"] = "Warning";

        var result = await controller.IndexAsync(ConfigurableFieldNames.Owner);

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<ConfigurationIndexViewModel>(view.Model);
        Assert.Equal("Saved", model.StatusMessage);
        Assert.Equal("Warning", model.ErrorMessage);
        Assert.Equal(ConfigurableFieldNames.Owner, model.ExpandedFieldName);
        Assert.Equal("UTC", model.DisplayTimeZoneId);
        Assert.NotEmpty(model.Fields);
    }

    [Fact]
    public async Task AiConfigurationSaveProviderPersistsProviderAndRedirectsAsync()
    {
        await using var fixture = await TestFixture.CreateAsync();
        using var controller = fixture.CreateAiConfigurationController();

        var result = await controller.SaveProviderAsync(new AiProviderConfigurationInputModel
        {
            Name = " Open WebUI Lab ",
            ProviderType = AiProviderType.OpenWebUi,
            Endpoint = " http://localhost:3000/api/chat/completions ",
            Model = " gpt-oss:latest ",
            InputCostPerMillionTokensSek = 8.5m,
            OutputCostPerMillionTokensSek = 24.75m,
            ApiKey = "lab-key",
            TimeoutSeconds = 180
        });

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Index", redirect.ActionName);

        var provider = await fixture.DbContext.AiProviderConfigurations.SingleAsync();
        var settings = await fixture.DbContext.AppSettings
            .OrderBy(x => x.Key)
            .ToDictionaryAsync(x => x.Key, x => x.Value);

        Assert.Equal("Open WebUI Lab", provider.Name);
        Assert.Equal(AiProviderType.OpenWebUi, provider.ProviderType);
        Assert.Equal("http://localhost:3000/api/chat/completions", provider.Endpoint);
        Assert.Equal("gpt-oss:latest", provider.Model);
        Assert.Equal(8.5m, provider.InputCostPerMillionTokensSek);
        Assert.Equal(24.75m, provider.OutputCostPerMillionTokensSek);
        Assert.Equal(180, provider.TimeoutSeconds);
        Assert.True(provider.IsActive);
        Assert.StartsWith("dp:", settings[$"AiProvider.{provider.Id}.ApiKey"], StringComparison.Ordinal);
    }

    [Fact]
    public async Task AiConfigurationSetLookupEnabledRejectsIncompleteConfigurationAsync()
    {
        await using var fixture = await TestFixture.CreateAsync();
        using var controller = fixture.CreateAiConfigurationController();

        var result = await controller.SetLookupEnabledAsync(true);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Index", redirect.ActionName);
        Assert.Equal("Save and enable a provider with endpoint, model, and API key before enabling AI mapping lookup.", controller.TempData["AiConfigurationErrorMessage"]);
    }

    [Fact]
    public async Task AiConfigurationSetProviderEnabledDisablesOtherProvidersAsync()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var providerA = new AiProviderConfiguration
        {
            Name = "Open WebUI",
            ProviderType = AiProviderType.OpenWebUi,
            Endpoint = "http://localhost:3000/api/chat/completions",
            Model = "gpt-oss:latest",
            TimeoutSeconds = 120,
            IsActive = true
        };
        var providerB = new AiProviderConfiguration
        {
            Name = "OpenAI",
            ProviderType = AiProviderType.OpenAiApi,
            Endpoint = "https://api.openai.com/v1/chat/completions",
            Model = "gpt-4.1",
            TimeoutSeconds = 120
        };
        await fixture.DbContext.AiProviderConfigurations.AddRangeAsync(providerA, providerB);
        await fixture.DbContext.SaveChangesAsync();

        using var controller = fixture.CreateAiConfigurationController();

        var result = await controller.SetProviderEnabledAsync(providerB.Id, true);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Index", redirect.ActionName);

        var providers = await fixture.DbContext.AiProviderConfigurations
            .OrderBy(x => x.Id)
            .ToListAsync();

        Assert.False(providers[0].IsActive);
        Assert.True(providers[1].IsActive);
        Assert.Equal("'OpenAI' is now enabled. Other providers were disabled.", controller.TempData["AiConfigurationStatusMessage"]);
    }

    [Fact]
    public async Task AiConfigurationIndexHidesEditorUntilAdminStartsEditingAsync()
    {
        await using var fixture = await TestFixture.CreateAsync();
        using var controller = fixture.CreateAiConfigurationController();

        var result = await controller.IndexAsync();

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<AiMappingAdminIndexViewModel>(view.Model);
        Assert.False(model.ShowEditor);
        Assert.False(model.IsCreatingProvider);
    }

    [Fact]
    public async Task AiConfigurationIndexShowsBlankEditorForNewProviderAsync()
    {
        await using var fixture = await TestFixture.CreateAsync();
        using var controller = fixture.CreateAiConfigurationController();

        var result = await controller.IndexAsync(createNewProvider: true);

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<AiMappingAdminIndexViewModel>(view.Model);
        Assert.True(model.ShowEditor);
        Assert.True(model.IsCreatingProvider);
        Assert.Null(model.Editor.Id);
    }

    [Fact]
    public async Task AiConfigurationIndexShowsSelectedProviderWhenEditingAsync()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var provider = new AiProviderConfiguration
        {
            Name = "Open WebUI",
            ProviderType = AiProviderType.OpenWebUi,
            Endpoint = "http://localhost:3000/api/chat/completions",
            Model = "gpt-oss:latest",
            TimeoutSeconds = 120
        };
        await fixture.DbContext.AiProviderConfigurations.AddAsync(provider);
        await fixture.DbContext.SaveChangesAsync();

        using var controller = fixture.CreateAiConfigurationController();

        var result = await controller.IndexAsync(editProviderId: provider.Id);

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<AiMappingAdminIndexViewModel>(view.Model);
        Assert.True(model.ShowEditor);
        Assert.False(model.IsCreatingProvider);
        Assert.Equal(provider.Id, model.Editor.Id);
    }

    [Fact]
    public async Task VerifyCatalogueImportReturnsErrorReviewWhenWorkbookMissingAsync()
    {
        await using var fixture = await TestFixture.CreateAsync();
        using var controller = fixture.CreateConfigurationController();

        var result = await controller.VerifyCatalogueImportAsync(null);

        var view = Assert.IsType<ViewResult>(result);
        Assert.Equal("ImportData", view.ViewName);
        var model = Assert.IsType<ConfigurationIndexViewModel>(view.Model);
        Assert.Equal("Choose an .xlsx workbook before verifying the import.", Assert.Single(model.CatalogueImportReview.Verification!.Errors));
    }

    [Fact]
    public async Task VerifyCatalogueImportKeepsSelectedModelInErrorReviewAsync()
    {
        await using var fixture = await TestFixture.CreateAsync();
        using var controller = fixture.CreateConfigurationController();

        var result = await controller.VerifyCatalogueImportAsync(null, ReferenceModelKind.Brm);

        var view = Assert.IsType<ViewResult>(result);
        Assert.Equal("ImportData", view.ViewName);
        var model = Assert.IsType<ConfigurationIndexViewModel>(view.Model);
        Assert.Equal(ReferenceModelKind.Brm, model.CatalogueImportModelKind);
        Assert.Equal(ReferenceModelKind.Brm, model.CatalogueImportReview.ModelKind);
        Assert.Equal(ReferenceModelKind.Brm, model.CatalogueImportReview.Verification!.ModelKind);
    }

    [Fact]
    public async Task VerifyCatalogueImportReturnsErrorReviewWhenExtensionInvalidAsync()
    {
        await using var fixture = await TestFixture.CreateAsync();
        using var controller = fixture.CreateConfigurationController();
        await using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("not-a-workbook"));
        var file = new FormFile(stream, 0, stream.Length, "file", "catalogue.csv");

        var result = await controller.VerifyCatalogueImportAsync(file);

        var view = Assert.IsType<ViewResult>(result);
        Assert.Equal("ImportData", view.ViewName);
        var model = Assert.IsType<ConfigurationIndexViewModel>(view.Model);
        Assert.Equal("Only Excel .xlsx workbooks are supported.", Assert.Single(model.CatalogueImportReview.Verification!.Errors));
    }

    [Fact]
    public async Task VerifyCatalogueImportReturnsVerificationErrorsForInvalidWorkbookContentAsync()
    {
        await using var fixture = await TestFixture.CreateAsync();
        using var controller = fixture.CreateConfigurationController();
        await using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("not-a-valid-zip-workbook"));
        var file = new FormFile(stream, 0, stream.Length, "file", "catalogue.xlsx");

        var result = await controller.VerifyCatalogueImportAsync(file);

        var view = Assert.IsType<ViewResult>(result);
        Assert.Equal("ImportData", view.ViewName);
        var model = Assert.IsType<ConfigurationIndexViewModel>(view.Model);
        Assert.Equal("catalogue.xlsx", model.CatalogueImportReview.UploadedFileName);
        Assert.Null(model.CatalogueImportReview.PendingImportToken);
        Assert.NotEmpty(model.CatalogueImportReview.Verification!.Errors);
        Assert.Empty(Directory.GetFiles(Path.Combine(fixture.ContentRootPath, "App_Data", "PendingImports", "catalogue"), "*.xlsx", SearchOption.TopDirectoryOnly));
    }

    [Fact]
    public async Task ImportVerifiedCatalogueWithMissingTokenRedirectsWithErrorAsync()
    {
        await using var fixture = await TestFixture.CreateAsync();
        using var controller = fixture.CreateConfigurationController();

        var result = await controller.ImportVerifiedCatalogueAsync("");

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("ImportData", redirect.ActionName);
        Assert.Equal("Verify a catalogue workbook before importing it.", controller.TempData["ConfigurationError"]);
    }

    [Fact]
    public async Task ImportVerifiedCatalogueWithMissingFileRedirectsWithErrorAsync()
    {
        await using var fixture = await TestFixture.CreateAsync();
        using var controller = fixture.CreateConfigurationController();

        var result = await controller.ImportVerifiedCatalogueAsync("missing-token");

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("ImportData", redirect.ActionName);
        Assert.Equal("The verified catalogue workbook is no longer available. Upload it again.", controller.TempData["ConfigurationError"]);
    }

    [Fact]
    public async Task ImportVerifiedCatalogueReturnsViewWhenWorkbookVerificationFailsAsync()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var pendingPath = Path.Combine(fixture.ContentRootPath, "App_Data", "PendingImports", "catalogue", "bad-token.xlsx");
        Directory.CreateDirectory(Path.GetDirectoryName(pendingPath)!);
        await File.WriteAllTextAsync(pendingPath, "not-a-valid-zip-workbook");
        using var controller = fixture.CreateConfigurationController();

        var result = await controller.ImportVerifiedCatalogueAsync("bad-token");

        var view = Assert.IsType<ViewResult>(result);
        Assert.Equal("ImportData", view.ViewName);
        var model = Assert.IsType<ConfigurationIndexViewModel>(view.Model);
        Assert.NotEmpty(model.CatalogueImportReview.Verification!.Errors);
        Assert.False(File.Exists(pendingPath));
    }

    [Fact]
    public async Task ImportVerifiedCatalogueImportsWorkbookAndWritesStatusAsync()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var pendingPath = Path.Combine(fixture.ContentRootPath, "App_Data", "PendingImports", "catalogue", "good-token.xlsx");
        WorkbookTestFileFactory.WriteWorkbook(
            pendingPath,
            new WorkbookSheet(
                "TRM Domain",
                [
                    ["Source", "Code", "Name", "Description", "Comments"],
                    ["Workbook", "TD001", "Technology", "Domain description", "Domain comments"]
                ]),
            new WorkbookSheet(
                "TRM Capability",
                [
                    ["Source", "Code", "Name", "Parent Domain", "Description", "Comments"],
                    ["Workbook", "TP001", "Observability", "TD001 Technology", "Capability description", "Capability comments"]
                ]),
            new WorkbookSheet(
                "TRM Component",
                [
                    ["Source", "Code", "Name", "Parent Capability", "Description", "Comments", "Product examples"],
                    ["Workbook", "TC001", "Monitoring", "TP001 Observability", "Component description", "Component comments", "Graylog"]
                ]));

        using var controller = fixture.CreateConfigurationController();
        var result = await controller.ImportVerifiedCatalogueAsync("good-token");

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("ImportData", redirect.ActionName);
        Assert.False(File.Exists(pendingPath));
        Assert.Equal(
            "TRM catalogue imported. Domains +1/0 updated, capabilities +1/0 updated, components +1/0 updated.",
            controller.TempData["ConfigurationStatusMessage"]);
        Assert.Equal(1, await fixture.DbContext.TrmDomains.CountAsync());
        Assert.Equal(1, await fixture.DbContext.TrmCapabilities.CountAsync());
        Assert.Equal(1, await fixture.DbContext.TrmComponents.CountAsync());
        Assert.Contains(
            await fixture.DbContext.AuditLogEntries.Select(entry => entry.Action).ToListAsync(),
            action => string.Equals(action, "ImportCatalogue", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ImportVerifiedCatalogueImportsArmWorkbookWhenModelSelectedAsync()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var pendingPath = Path.Combine(fixture.ContentRootPath, "App_Data", "PendingImports", "catalogue", "arm-token.xlsx");
        WorkbookTestFileFactory.WriteWorkbook(
            pendingPath,
            new WorkbookSheet(
                "ARM Domain",
                [
                    ["Source", "Code", "Name", "Description", "Comments"],
                    ["Workbook", "AD001", "Business Apps", "Domain description", "Domain comments"]
                ]),
            new WorkbookSheet(
                "ARM Capability",
                [
                    ["Source", "Code", "Name", "Parent Domain", "Description", "Comments"],
                    ["Workbook", "AP001", "Case Management", "AD001 Business Apps", "Capability description", "Capability comments"]
                ]),
            new WorkbookSheet(
                "ARM Component",
                [
                    ["Source", "Code", "Name", "Parent Capability", "Description", "Comments", "Product examples"],
                    ["Workbook", "AC001", "Workflow Engine", "AP001 Case Management", "Component description", "Component comments", "Contoso Workflow"]
                ]));

        using var controller = fixture.CreateConfigurationController();
        var result = await controller.ImportVerifiedCatalogueAsync("arm-token", ReferenceModelKind.Arm);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("ImportData", redirect.ActionName);
        Assert.False(File.Exists(pendingPath));
        Assert.Equal(
            "ARM catalogue imported. Domains +1/0 updated, capabilities +1/0 updated, components +1/0 updated.",
            controller.TempData["ConfigurationStatusMessage"]);
        Assert.Equal(1, await fixture.DbContext.ArmDomains.CountAsync(x => x.Code == "AD001"));
        Assert.Equal(1, await fixture.DbContext.ArmCapabilities.CountAsync(x => x.Code == "AP001"));
        Assert.Equal(1, await fixture.DbContext.ArmComponents.CountAsync(x => x.Code == "AC001"));
        Assert.Equal(0, await fixture.DbContext.TrmDomains.CountAsync(x => x.Code == "AD001"));
    }

    [Fact]
    public async Task ImportVerifiedCatalogueImportsBrmWorkbookWhenModelSelectedAsync()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var pendingPath = Path.Combine(fixture.ContentRootPath, "App_Data", "PendingImports", "catalogue", "brm-token.xlsx");
        WorkbookTestFileFactory.WriteWorkbook(
            pendingPath,
            new WorkbookSheet(
                "BRM",
                [
                    ["Title", "Capability Type", "Capability Level", "Value Chain", "Value Chain Segment", "Capability Code", "Capability Name", "Parent Capability", "Capability Description", "Capability Notes", "Capability Assessment", "Display Sequence"],
                    ["Business Capability", "Primary", "1", "Operations", "Fulfilment", "BC001", "Order Handling", "", "Level 1 description", "Level 1 notes", "High", "10"],
                    ["Business Capability", "Primary", "2", "Operations", "Fulfilment", "BC002", "Order Capture", "BC001 Order Handling", "Level 2 description", "Level 2 notes", "Medium", "20"]
                ]));

        using var controller = fixture.CreateConfigurationController();
        var result = await controller.ImportVerifiedCatalogueAsync("brm-token", ReferenceModelKind.Brm);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("ImportData", redirect.ActionName);
        Assert.False(File.Exists(pendingPath));
        Assert.Equal(
            "BRM catalogue imported. Groups +1/0 updated, level 1 capabilities +1/0 updated, level 2 capabilities +1/0 updated.",
            controller.TempData["ConfigurationStatusMessage"]);
        Assert.Equal(1, await fixture.DbContext.BrmDomains.CountAsync());
        Assert.Equal(1, await fixture.DbContext.BrmCapabilities.CountAsync(x => x.Code == "BC001"));
        Assert.Equal(1, await fixture.DbContext.BrmComponents.CountAsync(x => x.Code == "BC002"));
        Assert.Equal(0, await fixture.DbContext.TrmComponents.CountAsync(x => x.Code == "BC002"));
    }

    [Fact]
    public async Task AbortCatalogueImportDeletesPendingWorkbookAndWritesStatusAsync()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var token = "catalogue-token";
        var pendingPath = Path.Combine(fixture.ContentRootPath, "App_Data", "PendingImports", "catalogue", token + ".xlsx");
        Directory.CreateDirectory(Path.GetDirectoryName(pendingPath)!);
        await File.WriteAllTextAsync(pendingPath, "pending");

        using var controller = fixture.CreateConfigurationController();
        var result = await controller.AbortCatalogueImportAsync(token);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("ImportData", redirect.ActionName);
        Assert.False(File.Exists(pendingPath));
        Assert.Equal("TRM catalogue import was aborted.", controller.TempData["ConfigurationStatusMessage"]);
    }

    [Fact]
    public async Task AbortCatalogueImportWithBlankTokenStillWritesStatusAsync()
    {
        await using var fixture = await TestFixture.CreateAsync();

        using var controller = fixture.CreateConfigurationController();
        var result = await controller.AbortCatalogueImportAsync("   ");

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("ImportData", redirect.ActionName);
        Assert.Equal("TRM catalogue import was aborted.", controller.TempData["ConfigurationStatusMessage"]);
    }

    [Fact]
    public async Task VerifyProductImportReturnsErrorReviewWhenFileMissingAsync()
    {
        await using var fixture = await TestFixture.CreateAsync();
        using var controller = fixture.CreateConfigurationController();

        var result = await controller.VerifyProductImportAsync(null);

        var view = Assert.IsType<ViewResult>(result);
        Assert.Equal("ImportData", view.ViewName);
        var model = Assert.IsType<ConfigurationIndexViewModel>(view.Model);
        Assert.Equal("Choose a CSV file before verifying the import.", Assert.Single(model.ProductImportReview.Verification!.Errors));
    }

    [Fact]
    public async Task AddOptionRejectsDuplicateValueIgnoringCaseAsync()
    {
        await using var fixture = await TestFixture.CreateAsync();
        fixture.DbContext.ConfigurableFieldOptions.Add(new ConfigurableFieldOption
        {
            FieldName = ConfigurableFieldNames.Owner,
            Value = "Team Blue",
            SortOrder = 1
        });
        await fixture.DbContext.SaveChangesAsync();

        using var controller = fixture.CreateConfigurationController();

        var result = await controller.AddOptionAsync(new AddConfigurationOptionInputModel
        {
            FieldName = ConfigurableFieldNames.Owner,
            Value = "team blue"
        });

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Index", redirect.ActionName);
        Assert.Equal(ConfigurableFieldNames.Owner, Assert.IsType<string>(redirect.RouteValues!["expandedFieldName"]));
        Assert.Equal(1, await fixture.DbContext.ConfigurableFieldOptions.CountAsync());
        Assert.Equal("Owner value 'team blue' already exists.", controller.TempData["ConfigurationError"]);
    }

    [Fact]
    public async Task UpdateOptionOrderReordersOptionsAndRenumbersSequentiallyAsync()
    {
        await using var fixture = await TestFixture.CreateAsync();
        await fixture.DbContext.ConfigurableFieldOptions.AddRangeAsync(
            new ConfigurableFieldOption
            {
                FieldName = ConfigurableFieldNames.Owner,
                Value = "Team A",
                SortOrder = 1,
                CreatedUtc = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc)
            },
            new ConfigurableFieldOption
            {
                FieldName = ConfigurableFieldNames.Owner,
                Value = "Team B",
                SortOrder = 2,
                CreatedUtc = new DateTime(2026, 3, 2, 0, 0, 0, DateTimeKind.Utc)
            },
            new ConfigurableFieldOption
            {
                FieldName = ConfigurableFieldNames.Owner,
                Value = "Team C",
                SortOrder = 3,
                CreatedUtc = new DateTime(2026, 3, 3, 0, 0, 0, DateTimeKind.Utc)
            });
        await fixture.DbContext.SaveChangesAsync();

        var optionToMove = await fixture.DbContext.ConfigurableFieldOptions.SingleAsync(x => x.Value == "Team C");
        using var controller = fixture.CreateConfigurationController();

        var result = await controller.UpdateOptionOrderAsync(new UpdateConfigurationOptionOrderInputModel
        {
            Id = optionToMove.Id,
            SortOrder = 1
        });

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Index", redirect.ActionName);
        Assert.Equal(ConfigurableFieldNames.Owner, Assert.IsType<string>(redirect.RouteValues!["expandedFieldName"]));

        var options = await fixture.DbContext.ConfigurableFieldOptions
            .OrderBy(x => x.SortOrder)
            .Select(x => new { x.Value, x.SortOrder })
            .ToListAsync();

        Assert.Equal(
            ["Team C", "Team A", "Team B"],
            options.Select(x => x.Value).ToArray());
        Assert.Equal([1, 2, 3], options.Select(x => x.SortOrder).ToArray());
        Assert.Equal("Owner order was updated.", controller.TempData["ConfigurationStatusMessage"]);
    }

    [Fact]
    public async Task UpdateOptionUpdatesExistingValueAndWritesAuditLogAsync()
    {
        await using var fixture = await TestFixture.CreateAsync();
        await fixture.DbContext.ConfigurableFieldOptions.AddAsync(
            new ConfigurableFieldOption
            {
                FieldName = ConfigurableFieldNames.Owner,
                Value = "Team Blue",
                SortOrder = 1
            });
        await fixture.DbContext.SaveChangesAsync();

        var option = await fixture.DbContext.ConfigurableFieldOptions.SingleAsync();
        using var controller = fixture.CreateConfigurationController();

        var result = await controller.UpdateOptionAsync(new UpdateConfigurationOptionValueInputModel
        {
            Id = option.Id,
            Value = " Team Azure "
        });

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Index", redirect.ActionName);
        Assert.Equal(ConfigurableFieldNames.Owner, Assert.IsType<string>(redirect.RouteValues!["expandedFieldName"]));

        var updated = await fixture.DbContext.ConfigurableFieldOptions.SingleAsync();
        var audit = await fixture.DbContext.AuditLogEntries.SingleAsync();

        Assert.Equal("Team Azure", updated.Value);
        Assert.Equal("Owner value updated to 'Team Azure'.", controller.TempData["ConfigurationStatusMessage"]);
        Assert.Equal("Update", audit.Action);
    }

    [Fact]
    public async Task UpdateOptionRejectsDuplicateValueIgnoringCaseAsync()
    {
        await using var fixture = await TestFixture.CreateAsync();
        await fixture.DbContext.ConfigurableFieldOptions.AddRangeAsync(
            new ConfigurableFieldOption
            {
                FieldName = ConfigurableFieldNames.Owner,
                Value = "Team Blue",
                SortOrder = 1
            },
            new ConfigurableFieldOption
            {
                FieldName = ConfigurableFieldNames.Owner,
                Value = "Team Green",
                SortOrder = 2
            });
        await fixture.DbContext.SaveChangesAsync();

        var option = await fixture.DbContext.ConfigurableFieldOptions.SingleAsync(x => x.Value == "Team Green");
        using var controller = fixture.CreateConfigurationController();

        var result = await controller.UpdateOptionAsync(new UpdateConfigurationOptionValueInputModel
        {
            Id = option.Id,
            Value = "team blue"
        });

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Index", redirect.ActionName);
        Assert.Equal(ConfigurableFieldNames.Owner, Assert.IsType<string>(redirect.RouteValues!["expandedFieldName"]));
        Assert.Equal("Owner value 'team blue' already exists.", controller.TempData["ConfigurationError"]);
    }

    [Fact]
    public async Task ReorderOptionsUsesSubmittedOrderAndRenumbersSequentiallyAsync()
    {
        await using var fixture = await TestFixture.CreateAsync();
        await fixture.DbContext.ConfigurableFieldOptions.AddRangeAsync(
            new ConfigurableFieldOption
            {
                FieldName = ConfigurableFieldNames.Owner,
                Value = "Team A",
                SortOrder = 1
            },
            new ConfigurableFieldOption
            {
                FieldName = ConfigurableFieldNames.Owner,
                Value = "Team B",
                SortOrder = 2
            },
            new ConfigurableFieldOption
            {
                FieldName = ConfigurableFieldNames.Owner,
                Value = "Team C",
                SortOrder = 3
            });
        await fixture.DbContext.SaveChangesAsync();

        var options = await fixture.DbContext.ConfigurableFieldOptions.OrderBy(x => x.SortOrder).ToListAsync();
        using var controller = fixture.CreateConfigurationController();

        var result = await controller.ReorderOptionsAsync(new ReorderConfigurationOptionsInputModel
        {
            FieldName = ConfigurableFieldNames.Owner,
            OrderedIds = [options[2].Id, options[0].Id, options[1].Id]
        });

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Index", redirect.ActionName);
        Assert.Equal(ConfigurableFieldNames.Owner, Assert.IsType<string>(redirect.RouteValues!["expandedFieldName"]));

        var reordered = await fixture.DbContext.ConfigurableFieldOptions
            .OrderBy(x => x.SortOrder)
            .Select(x => new { x.Value, x.SortOrder })
            .ToListAsync();

        Assert.Equal(["Team C", "Team A", "Team B"], reordered.Select(x => x.Value).ToArray());
        Assert.Equal([1, 2, 3], reordered.Select(x => x.SortOrder).ToArray());
        Assert.Equal("Owner order was updated.", controller.TempData["ConfigurationStatusMessage"]);
    }

    [Fact]
    public async Task DeleteOptionRemovesOptionNormalizesSortOrderAndWritesAuditLogAsync()
    {
        await using var fixture = await TestFixture.CreateAsync();
        await fixture.DbContext.ConfigurableFieldOptions.AddRangeAsync(
            new ConfigurableFieldOption
            {
                FieldName = ConfigurableFieldNames.Owner,
                Value = "Team A",
                SortOrder = 1
            },
            new ConfigurableFieldOption
            {
                FieldName = ConfigurableFieldNames.Owner,
                Value = "Team B",
                SortOrder = 2
            },
            new ConfigurableFieldOption
            {
                FieldName = ConfigurableFieldNames.Owner,
                Value = "Team C",
                SortOrder = 3
            });
        await fixture.DbContext.SaveChangesAsync();

        var option = await fixture.DbContext.ConfigurableFieldOptions.SingleAsync(x => x.Value == "Team B");
        using var controller = fixture.CreateConfigurationController();

        var result = await controller.DeleteOptionAsync(option.Id);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Index", redirect.ActionName);
        Assert.Equal(ConfigurableFieldNames.Owner, Assert.IsType<string>(redirect.RouteValues!["expandedFieldName"]));

        var options = await fixture.DbContext.ConfigurableFieldOptions
            .OrderBy(x => x.SortOrder)
            .Select(x => new { x.Value, x.SortOrder })
            .ToListAsync();
        var audit = await fixture.DbContext.AuditLogEntries.SingleAsync();

        Assert.Equal(["Team A", "Team C"], options.Select(x => x.Value).ToArray());
        Assert.Equal([1, 2], options.Select(x => x.SortOrder).ToArray());
        Assert.Equal("Owner value 'Team B' was removed.", controller.TempData["ConfigurationStatusMessage"]);
        Assert.Equal("Delete", audit.Action);
    }

    [Fact]
    public async Task UpdateDisplayTimeZonePersistsSettingAndWritesAuditLogAsync()
    {
        await using var fixture = await TestFixture.CreateAsync();
        using var controller = fixture.CreateConfigurationController();

        var result = await controller.UpdateDisplayTimeZoneAsync(new UpdateDisplayTimeZoneInputModel
        {
            TimeZoneId = "UTC"
        });

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Index", redirect.ActionName);

        var setting = await fixture.DbContext.AppSettings.SingleAsync(x => x.Key == AppSettingKeys.DisplayTimeZone);
        var audit = await fixture.DbContext.AuditLogEntries.SingleAsync();

        Assert.Equal("UTC", setting.Value);
        Assert.Equal("Display time zone updated to 'UTC'.", controller.TempData["ConfigurationStatusMessage"]);
        Assert.Equal("UpdateDisplayTimeZone", audit.Action);
        Assert.Equal(nameof(AppSetting), audit.EntityType);
    }

    [Fact]
    public async Task UpdateDisplayTimeZoneRejectsUnknownTimeZoneAsync()
    {
        await using var fixture = await TestFixture.CreateAsync();
        using var controller = fixture.CreateConfigurationController();

        var result = await controller.UpdateDisplayTimeZoneAsync(new UpdateDisplayTimeZoneInputModel
        {
            TimeZoneId = "Not/AZone"
        });

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Index", redirect.ActionName);
        Assert.Equal("The time zone 'Not/AZone' is not available on this server.", controller.TempData["ConfigurationError"]);
    }

    [Fact]
    public async Task SetRemoteSqlImportEnabledDisablesConfiguredImportAndWritesStatusAsync()
    {
        await using var fixture = await TestFixture.CreateAsync();
        await fixture.DbContext.AppSettings.AddRangeAsync(
            new AppSetting { Key = AppSettingKeys.RemoteSqlImportServerName, Value = "sql.example.com" },
            new AppSetting { Key = AppSettingKeys.RemoteSqlImportDatabaseName, Value = "Herm" },
            new AppSetting { Key = AppSettingKeys.RemoteSqlImportIsEnabled, Value = bool.TrueString.ToLowerInvariant() });
        await fixture.DbContext.SaveChangesAsync();

        using var controller = fixture.CreateConfigurationController();
        var result = await controller.SetRemoteSqlImportEnabledAsync(false);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("ImportData", redirect.ActionName);
        Assert.Equal(
            "Remote SQL import disabled. Scheduled and manual imports will be skipped until you enable it again.",
            controller.TempData["ConfigurationStatusMessage"]);
        Assert.Equal(
            bool.FalseString,
            await fixture.DbContext.AppSettings.Where(x => x.Key == AppSettingKeys.RemoteSqlImportIsEnabled).Select(x => x.Value).SingleAsync());
        Assert.Contains(
            await fixture.DbContext.AuditLogEntries.Select(x => x.Action).ToListAsync(),
            action => string.Equals(action, "DisableImport", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RunRemoteSqlImportNowReturnsErrorWhenImportIsDisabledAsync()
    {
        await using var fixture = await TestFixture.CreateAsync();
        await fixture.DbContext.AppSettings.AddRangeAsync(
            new AppSetting { Key = AppSettingKeys.RemoteSqlImportServerName, Value = "sql.example.com" },
            new AppSetting { Key = AppSettingKeys.RemoteSqlImportDatabaseName, Value = "Herm" },
            new AppSetting { Key = AppSettingKeys.RemoteSqlImportIsEnabled, Value = bool.FalseString.ToLowerInvariant() });
        await fixture.DbContext.SaveChangesAsync();

        using var controller = fixture.CreateConfigurationController();
        var result = await controller.RunRemoteSqlImportNowAsync();

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("ImportData", redirect.ActionName);
        Assert.Equal("Remote SQL import is disabled. Enable it before running an import.", controller.TempData["ConfigurationError"]);
    }

    [Fact]
    public async Task ClearRemoteSqlImportConfigurationRemovesStoredSettingsAndCredentialsAsync()
    {
        await using var fixture = await TestFixture.CreateAsync();
        await fixture.DbContext.AppSettings.AddRangeAsync(
            new AppSetting { Key = AppSettingKeys.RemoteSqlImportServerName, Value = "sql.example.com" },
            new AppSetting { Key = AppSettingKeys.RemoteSqlImportDatabaseName, Value = "Herm" },
            new AppSetting { Key = AppSettingKeys.RemoteSqlImportIsEnabled, Value = bool.FalseString.ToLowerInvariant() },
            new AppSetting { Key = AppSettingKeys.RemoteSqlImportScheduleHours, Value = "6" },
            new AppSetting { Key = AppSettingKeys.RemoteSqlImportUserName, Value = "dp:user" },
            new AppSetting { Key = AppSettingKeys.RemoteSqlImportPassword, Value = "dp:password" },
            new AppSetting { Key = AppSettingKeys.RemoteSqlImportLastStatus, Value = "Failed" });
        await fixture.DbContext.SaveChangesAsync();

        using var controller = fixture.CreateConfigurationController();
        var result = await controller.ClearRemoteSqlImportConfigurationAsync();

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("ImportData", redirect.ActionName);
        Assert.Equal("Remote SQL configuration was cleared from the database.", controller.TempData["ConfigurationStatusMessage"]);
        Assert.Empty(await fixture.DbContext.AppSettings.Where(x => x.Key.StartsWith("RemoteSqlImport.")).ToListAsync());
        Assert.Contains(
            await fixture.DbContext.AuditLogEntries.Select(x => x.Action).ToListAsync(),
            action => string.Equals(action, "ClearConfiguration", StringComparison.Ordinal));
    }

    [Fact]
    public async Task VerifyProductImportReturnsErrorReviewWhenFileExtensionIsInvalidAsync()
    {
        await using var fixture = await TestFixture.CreateAsync();
        using var controller = fixture.CreateConfigurationController();
        var bytes = System.Text.Encoding.UTF8.GetBytes("not-a-csv");
        await using var stream = new MemoryStream(bytes);
        var file = new FormFile(stream, 0, bytes.Length, "file", "relationships.txt");

        var result = await controller.VerifyProductImportAsync(file);

        var view = Assert.IsType<ViewResult>(result);
        Assert.Equal("ImportData", view.ViewName);
        var model = Assert.IsType<ConfigurationIndexViewModel>(view.Model);
        Assert.True(model.ProductImportReview.HasReview);
        Assert.Equal("relationships.txt", model.ProductImportReview.UploadedFileName);
        Assert.Equal("Only .csv files are supported for product import.", Assert.Single(model.ProductImportReview.Verification!.Errors));
    }

    [Fact]
    public async Task VerifyProductImportReturnsVerificationErrorsForInvalidCsvHeaderAsync()
    {
        await using var fixture = await TestFixture.CreateAsync();
        using var controller = fixture.CreateConfigurationController();
        var bytes = System.Text.Encoding.UTF8.GetBytes("wrong;header\nvalue");
        await using var stream = new MemoryStream(bytes);
        var file = new FormFile(stream, 0, bytes.Length, "file", "relationships.csv");

        var result = await controller.VerifyProductImportAsync(file);

        var view = Assert.IsType<ViewResult>(result);
        Assert.Equal("ImportData", view.ViewName);
        var model = Assert.IsType<ConfigurationIndexViewModel>(view.Model);
        Assert.Equal("relationships.csv", model.ProductImportReview.UploadedFileName);
        Assert.Null(model.ProductImportReview.PendingImportToken);
        Assert.Equal("The CSV header must be 'MODEL;DOMAIN;CAPABILITY;COMPONENT;PRODUCT'.", Assert.Single(model.ProductImportReview.Verification!.Errors));
        Assert.Empty(Directory.GetFiles(Path.Combine(fixture.ContentRootPath, "App_Data", "PendingImports", "products"), "*.csv", SearchOption.TopDirectoryOnly));
    }

    [Fact]
    public async Task ImportVerifiedProductsWithMissingTokenRedirectsWithErrorAsync()
    {
        await using var fixture = await TestFixture.CreateAsync();
        using var controller = fixture.CreateConfigurationController();

        var result = await controller.ImportVerifiedProductsAsync("");

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("ImportData", redirect.ActionName);
        Assert.Equal("Verify a product CSV before importing it.", controller.TempData["ConfigurationError"]);
    }

    [Fact]
    public async Task ImportVerifiedProductsWithMissingFileRedirectsWithErrorAsync()
    {
        await using var fixture = await TestFixture.CreateAsync();
        using var controller = fixture.CreateConfigurationController();

        var result = await controller.ImportVerifiedProductsAsync("missing-token");

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("ImportData", redirect.ActionName);
        Assert.Equal("The verified product CSV is no longer available. Upload it again.", controller.TempData["ConfigurationError"]);
    }

    [Fact]
    public async Task ImportVerifiedProductsReturnsViewWhenCsvVerificationFailsAsync()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var pendingPath = Path.Combine(fixture.ContentRootPath, "App_Data", "PendingImports", "products", "bad-token.csv");
        Directory.CreateDirectory(Path.GetDirectoryName(pendingPath)!);
        await File.WriteAllTextAsync(pendingPath, "wrong;header\nvalue");
        using var controller = fixture.CreateConfigurationController();

        var result = await controller.ImportVerifiedProductsAsync("bad-token");

        var view = Assert.IsType<ViewResult>(result);
        Assert.Equal("ImportData", view.ViewName);
        var model = Assert.IsType<ConfigurationIndexViewModel>(view.Model);
        Assert.Equal("The CSV header must be 'MODEL;DOMAIN;CAPABILITY;COMPONENT;PRODUCT'.", Assert.Single(model.ProductImportReview.Verification!.Errors));
        Assert.False(File.Exists(pendingPath));
    }

    [Fact]
    public async Task ImportVerifiedProductsImportsCsvAndWritesStatusAsync()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var domain = new TrmDomain { Code = "TD001", Name = "Cybersecurity" };
        var capability = new TrmCapability { Code = "TP001", Name = "Capability A", ParentDomain = domain, ParentDomainCode = domain.Code };
        var component = new TrmComponent { Code = "TC002", Name = "Monitoring & Alerting", ParentCapability = capability, ParentCapabilityCode = capability.Code };
        await fixture.DbContext.AddRangeAsync(domain, capability, component);
        await fixture.DbContext.SaveChangesAsync();

        var pendingPath = Path.Combine(fixture.ContentRootPath, "App_Data", "PendingImports", "products", "good-token.csv");
        Directory.CreateDirectory(Path.GetDirectoryName(pendingPath)!);
        await File.WriteAllTextAsync(
            pendingPath,
            "MODEL;DOMAIN;CAPABILITY;COMPONENT;PRODUCT\nHERM;TD001 Cybersecurity;TP001 Capability A;TC002 Monitoring & Alerting;Graylog");

        using var controller = fixture.CreateConfigurationController();
        var result = await controller.ImportVerifiedProductsAsync("good-token");

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("ImportData", redirect.ActionName);
        Assert.False(File.Exists(pendingPath));
        Assert.Equal(
            "Imported 1 new product(s), matched 0 existing product(s), created 1 mapping(s), and left 0 row(s) as product-only because the hierarchy did not match.",
            controller.TempData["ConfigurationStatusMessage"]);
        Assert.Equal(1, await fixture.DbContext.ProductCatalogItems.CountAsync());
        Assert.Equal(1, await fixture.DbContext.ProductMappings.CountAsync());
        Assert.Contains(
            await fixture.DbContext.AuditLogEntries.Select(entry => entry.Action).ToListAsync(),
            action => string.Equals(action, "ImportProducts", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AbortProductImportDeletesPendingCsvAndWritesStatusAsync()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var token = "product-token";
        var pendingPath = Path.Combine(fixture.ContentRootPath, "App_Data", "PendingImports", "products", token + ".csv");
        Directory.CreateDirectory(Path.GetDirectoryName(pendingPath)!);
        await File.WriteAllTextAsync(pendingPath, "pending");

        using var controller = fixture.CreateConfigurationController();
        var result = await controller.AbortProductImportAsync(token);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("ImportData", redirect.ActionName);
        Assert.False(File.Exists(pendingPath));
        Assert.Equal("Product import was aborted.", controller.TempData["ConfigurationStatusMessage"]);
    }

    [Fact]
    public async Task AddOptionRejectsUnsupportedFieldAsync()
    {
        await using var fixture = await TestFixture.CreateAsync();
        using var controller = fixture.CreateConfigurationController();

        var result = await controller.AddOptionAsync(new AddConfigurationOptionInputModel
        {
            FieldName = "UnknownField",
            Value = "Team Blue"
        });

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Index", redirect.ActionName);
        Assert.Equal("That field is not supported.", controller.TempData["ConfigurationError"]);
    }

    [Fact]
    public async Task AddOptionRejectsBlankValueAsync()
    {
        await using var fixture = await TestFixture.CreateAsync();
        using var controller = fixture.CreateConfigurationController();

        var result = await controller.AddOptionAsync(new AddConfigurationOptionInputModel
        {
            FieldName = ConfigurableFieldNames.Owner,
            Value = "   "
        });

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Index", redirect.ActionName);
        Assert.Equal(ConfigurableFieldNames.Owner, Assert.IsType<string>(redirect.RouteValues!["expandedFieldName"]));
        Assert.Equal("Enter a value before saving.", controller.TempData["ConfigurationError"]);
    }

    [Fact]
    public async Task UpdateOptionOrderMissingOptionRedirectsWithoutChangesAsync()
    {
        await using var fixture = await TestFixture.CreateAsync();
        using var controller = fixture.CreateConfigurationController();

        var result = await controller.UpdateOptionOrderAsync(new UpdateConfigurationOptionOrderInputModel { Id = 999, SortOrder = 1 });

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Index", redirect.ActionName);
    }

    [Fact]
    public async Task DeleteOptionMissingOptionRedirectsWithoutChangesAsync()
    {
        await using var fixture = await TestFixture.CreateAsync();
        using var controller = fixture.CreateConfigurationController();

        var result = await controller.DeleteOptionAsync(999);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Index", redirect.ActionName);
    }

    [Fact]
    public async Task UpdateDisplayTimeZoneRejectsBlankValueAsync()
    {
        await using var fixture = await TestFixture.CreateAsync();
        using var controller = fixture.CreateConfigurationController();

        var result = await controller.UpdateDisplayTimeZoneAsync(new UpdateDisplayTimeZoneInputModel { TimeZoneId = "   " });

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Index", redirect.ActionName);
        Assert.Equal("Choose a time zone before saving.", controller.TempData["ConfigurationError"]);
    }

    private sealed class TestFixture : IAsyncDisposable
    {
        private readonly SqliteConnection connection;
        private readonly TemporaryDirectory contentRoot;
        private readonly StubHttpMessageHandler aiHttpMessageHandler = new();
        private readonly HttpClient aiHttpClient;

        private TestFixture(SqliteConnection connection, TemporaryDirectory contentRoot, AppDbContext dbContext)
        {
            this.connection = connection;
            this.contentRoot = contentRoot;
            aiHttpClient = new HttpClient(aiHttpMessageHandler);
            DbContext = dbContext;
        }

        public AppDbContext DbContext { get; }

        public string ContentRootPath => contentRoot.Path;

        public static async Task<TestFixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();

            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(connection)
                .Options;

            var dbContext = new AppDbContext(options);
            await dbContext.Database.EnsureCreatedAsync();

            var contentRoot = new TemporaryDirectory();

            return new TestFixture(connection, contentRoot, dbContext);
        }

        public ChangeLogController CreateChangeLogController() => new(DbContext);

        public ConfigurationController CreateConfigurationController()
        {
            var appSettingsService = new AppSettingsService(DbContext);
            var auditLogService = new AuditLogService(DbContext);
            var protectedSettingsService = new ProtectedSettingsService(
                new EphemeralDataProtectionProvider(),
                appSettingsService,
                NullLogger<ProtectedSettingsService>.Instance);

            var controller = new ConfigurationController(
                DbContext,
                appSettingsService,
                new ConfigurableFieldService(DbContext),
                auditLogService,
                new RemoteSqlImportService(
                    DbContext,
                    appSettingsService,
                    protectedSettingsService,
                    auditLogService,
                    new RemoteSqlImportExecutionGate(),
                    NullLogger<RemoteSqlImportService>.Instance),
                new TrmWorkbookImportService(DbContext, new ComponentVersioningService(DbContext), new AuditLogService(DbContext)),
                new SampleRelationshipImportService(DbContext),
                new TestWebHostEnvironment(contentRoot.Path));

            controller.TempData = new TempDataDictionary(new DefaultHttpContext(), new TestTempDataProvider());
            return controller;
        }

        public AiConfigurationController CreateAiConfigurationController()
        {
            var httpContext = new DefaultHttpContext();
            var controller = new AiConfigurationController(CreateAiProductMappingService())
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = httpContext
                },
                TempData = new TempDataDictionary(httpContext, new TestTempDataProvider())
            };

            return controller;
        }

        private AiProductMappingService CreateAiProductMappingService()
        {
            var appSettingsService = new AppSettingsService(DbContext);
            return new AiProductMappingService(
                DbContext,
                appSettingsService,
                new ProtectedSettingsService(
                    new EphemeralDataProtectionProvider(),
                    appSettingsService,
                    NullLogger<ProtectedSettingsService>.Instance),
                new AuditLogService(DbContext),
                aiHttpClient,
                NullLogger<AiProductMappingService>.Instance);
        }

        public async ValueTask DisposeAsync()
        {
            aiHttpClient.Dispose();
            aiHttpMessageHandler.Dispose();
            await DbContext.DisposeAsync();
            await connection.DisposeAsync();
            contentRoot.Dispose();
        }
    }

    private sealed class TestTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();

        public void SaveTempData(HttpContext context, IDictionary<string, object> values)
        {
        }
    }

    private sealed class TestWebHostEnvironment(string contentRootPath) : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "HERM-MAPPER-APP.Tests";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = string.Empty;
        public string EnvironmentName { get; set; } = "Development";
        public string ContentRootPath { get; set; } = contentRootPath;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"herm-mapper-config-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"choices\":[{\"message\":{\"content\":\"summary: \\\"No suggestions\\\"\\nsuggestions[0]{component_id\\tconfidence\\treason}:\"}}]}", Encoding.UTF8, "application/json")
            });
    }
}
