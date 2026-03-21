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
using Xunit;

namespace HERMMapperApp.Tests.Controllers;

public sealed class ConfigurationAndChangeLogControllerTests
{
    [Fact]
    public async Task ChangeLogIndexFiltersBySearchAndOrdersNewestFirst()
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
        var result = await controller.Index("Product");

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<ChangeLogIndexViewModel>(view.Model);

        Assert.Equal("Product", model.Search);
        Assert.Equal(2, model.Entries.Count);
        Assert.Equal("VerifyProductImport", model.Entries[0].Action);
        Assert.Equal("Create", model.Entries[1].Action);
    }

    [Fact]
    public async Task ChangeLogIndexFiltersByActorUserName()
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
        var result = await controller.Index("ada");

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<ChangeLogIndexViewModel>(view.Model);

        var entry = Assert.Single(model.Entries);
        Assert.Equal("ada", entry.ActorUserName);
        Assert.Equal("Create", entry.Action);
    }

    [Fact]
    public async Task AddOptionCreatesOptionAndWritesAuditLog()
    {
        await using var fixture = await TestFixture.CreateAsync();
        using var controller = fixture.CreateConfigurationController();

        var result = await controller.AddOption(new AddConfigurationOptionInputModel
        {
            FieldName = " Owner ",
            Value = " Team Blue "
        });

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(ConfigurationController.Index), redirect.ActionName);
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
    public async Task IndexBuildsViewModelFromSettingsAndTempData()
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

        var result = await controller.Index(ConfigurableFieldNames.Owner);

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<ConfigurationIndexViewModel>(view.Model);
        Assert.Equal("Saved", model.StatusMessage);
        Assert.Equal("Warning", model.ErrorMessage);
        Assert.Equal(ConfigurableFieldNames.Owner, model.ExpandedFieldName);
        Assert.Equal("UTC", model.DisplayTimeZoneId);
        Assert.NotEmpty(model.Fields);
    }

    [Fact]
    public async Task VerifyCatalogueImportReturnsErrorReviewWhenWorkbookMissing()
    {
        await using var fixture = await TestFixture.CreateAsync();
        using var controller = fixture.CreateConfigurationController();

        var result = await controller.VerifyCatalogueImport(null);

        var view = Assert.IsType<ViewResult>(result);
        Assert.Equal("Index", view.ViewName);
        var model = Assert.IsType<ConfigurationIndexViewModel>(view.Model);
        Assert.Equal("Choose an .xlsx workbook before verifying the import.", Assert.Single(model.CatalogueImportReview.Verification!.Errors));
    }

    [Fact]
    public async Task VerifyCatalogueImportKeepsSelectedModelInErrorReview()
    {
        await using var fixture = await TestFixture.CreateAsync();
        using var controller = fixture.CreateConfigurationController();

        var result = await controller.VerifyCatalogueImport(null, ReferenceModelKind.Brm);

        var view = Assert.IsType<ViewResult>(result);
        Assert.Equal("Index", view.ViewName);
        var model = Assert.IsType<ConfigurationIndexViewModel>(view.Model);
        Assert.Equal(ReferenceModelKind.Brm, model.CatalogueImportModelKind);
        Assert.Equal(ReferenceModelKind.Brm, model.CatalogueImportReview.ModelKind);
        Assert.Equal(ReferenceModelKind.Brm, model.CatalogueImportReview.Verification!.ModelKind);
    }

    [Fact]
    public async Task VerifyCatalogueImportReturnsErrorReviewWhenExtensionInvalid()
    {
        await using var fixture = await TestFixture.CreateAsync();
        using var controller = fixture.CreateConfigurationController();
        await using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("not-a-workbook"));
        var file = new FormFile(stream, 0, stream.Length, "file", "catalogue.csv");

        var result = await controller.VerifyCatalogueImport(file);

        var view = Assert.IsType<ViewResult>(result);
        Assert.Equal("Index", view.ViewName);
        var model = Assert.IsType<ConfigurationIndexViewModel>(view.Model);
        Assert.Equal("Only Excel .xlsx workbooks are supported.", Assert.Single(model.CatalogueImportReview.Verification!.Errors));
    }

    [Fact]
    public async Task VerifyCatalogueImportReturnsVerificationErrorsForInvalidWorkbookContent()
    {
        await using var fixture = await TestFixture.CreateAsync();
        using var controller = fixture.CreateConfigurationController();
        await using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("not-a-valid-zip-workbook"));
        var file = new FormFile(stream, 0, stream.Length, "file", "catalogue.xlsx");

        var result = await controller.VerifyCatalogueImport(file);

        var view = Assert.IsType<ViewResult>(result);
        Assert.Equal("Index", view.ViewName);
        var model = Assert.IsType<ConfigurationIndexViewModel>(view.Model);
        Assert.Equal("catalogue.xlsx", model.CatalogueImportReview.UploadedFileName);
        Assert.Null(model.CatalogueImportReview.PendingImportToken);
        Assert.NotEmpty(model.CatalogueImportReview.Verification!.Errors);
        Assert.Empty(Directory.GetFiles(Path.Combine(fixture.ContentRootPath, "App_Data", "PendingImports", "catalogue"), "*.xlsx", SearchOption.TopDirectoryOnly));
    }

    [Fact]
    public async Task ImportVerifiedCatalogueWithMissingTokenRedirectsWithError()
    {
        await using var fixture = await TestFixture.CreateAsync();
        using var controller = fixture.CreateConfigurationController();

        var result = await controller.ImportVerifiedCatalogue("");

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(ConfigurationController.Index), redirect.ActionName);
        Assert.Equal("Verify a catalogue workbook before importing it.", controller.TempData["ConfigurationError"]);
    }

    [Fact]
    public async Task ImportVerifiedCatalogueWithMissingFileRedirectsWithError()
    {
        await using var fixture = await TestFixture.CreateAsync();
        using var controller = fixture.CreateConfigurationController();

        var result = await controller.ImportVerifiedCatalogue("missing-token");

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(ConfigurationController.Index), redirect.ActionName);
        Assert.Equal("The verified catalogue workbook is no longer available. Upload it again.", controller.TempData["ConfigurationError"]);
    }

    [Fact]
    public async Task ImportVerifiedCatalogueReturnsViewWhenWorkbookVerificationFails()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var pendingPath = Path.Combine(fixture.ContentRootPath, "App_Data", "PendingImports", "catalogue", "bad-token.xlsx");
        Directory.CreateDirectory(Path.GetDirectoryName(pendingPath)!);
        await File.WriteAllTextAsync(pendingPath, "not-a-valid-zip-workbook");
        using var controller = fixture.CreateConfigurationController();

        var result = await controller.ImportVerifiedCatalogue("bad-token");

        var view = Assert.IsType<ViewResult>(result);
        Assert.Equal("Index", view.ViewName);
        var model = Assert.IsType<ConfigurationIndexViewModel>(view.Model);
        Assert.NotEmpty(model.CatalogueImportReview.Verification!.Errors);
        Assert.False(File.Exists(pendingPath));
    }

    [Fact]
    public async Task ImportVerifiedCatalogueImportsWorkbookAndWritesStatus()
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
        var result = await controller.ImportVerifiedCatalogue("good-token");

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(ConfigurationController.Index), redirect.ActionName);
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
    public async Task ImportVerifiedCatalogueImportsArmWorkbookWhenModelSelected()
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
        var result = await controller.ImportVerifiedCatalogue("arm-token", ReferenceModelKind.Arm);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(ConfigurationController.Index), redirect.ActionName);
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
    public async Task ImportVerifiedCatalogueImportsBrmWorkbookWhenModelSelected()
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
        var result = await controller.ImportVerifiedCatalogue("brm-token", ReferenceModelKind.Brm);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(ConfigurationController.Index), redirect.ActionName);
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
    public async Task AbortCatalogueImportDeletesPendingWorkbookAndWritesStatus()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var token = "catalogue-token";
        var pendingPath = Path.Combine(fixture.ContentRootPath, "App_Data", "PendingImports", "catalogue", token + ".xlsx");
        Directory.CreateDirectory(Path.GetDirectoryName(pendingPath)!);
        await File.WriteAllTextAsync(pendingPath, "pending");

        using var controller = fixture.CreateConfigurationController();
        var result = await controller.AbortCatalogueImport(token);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(ConfigurationController.Index), redirect.ActionName);
        Assert.False(File.Exists(pendingPath));
        Assert.Equal("TRM catalogue import was aborted.", controller.TempData["ConfigurationStatusMessage"]);
    }

    [Fact]
    public async Task AbortCatalogueImportWithBlankTokenStillWritesStatus()
    {
        await using var fixture = await TestFixture.CreateAsync();

        using var controller = fixture.CreateConfigurationController();
        var result = await controller.AbortCatalogueImport("   ");

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(ConfigurationController.Index), redirect.ActionName);
        Assert.Equal("TRM catalogue import was aborted.", controller.TempData["ConfigurationStatusMessage"]);
    }

    [Fact]
    public async Task VerifyProductImportReturnsErrorReviewWhenFileMissing()
    {
        await using var fixture = await TestFixture.CreateAsync();
        using var controller = fixture.CreateConfigurationController();

        var result = await controller.VerifyProductImport(null);

        var view = Assert.IsType<ViewResult>(result);
        Assert.Equal("Index", view.ViewName);
        var model = Assert.IsType<ConfigurationIndexViewModel>(view.Model);
        Assert.Equal("Choose a CSV file before verifying the import.", Assert.Single(model.ProductImportReview.Verification!.Errors));
    }

    [Fact]
    public async Task AddOptionRejectsDuplicateValueIgnoringCase()
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

        var result = await controller.AddOption(new AddConfigurationOptionInputModel
        {
            FieldName = ConfigurableFieldNames.Owner,
            Value = "team blue"
        });

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(ConfigurationController.Index), redirect.ActionName);
        Assert.Equal(ConfigurableFieldNames.Owner, Assert.IsType<string>(redirect.RouteValues!["expandedFieldName"]));
        Assert.Equal(1, await fixture.DbContext.ConfigurableFieldOptions.CountAsync());
        Assert.Equal("Owner value 'team blue' already exists.", controller.TempData["ConfigurationError"]);
    }

    [Fact]
    public async Task UpdateOptionOrderReordersOptionsAndRenumbersSequentially()
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

        var result = await controller.UpdateOptionOrder(new UpdateConfigurationOptionOrderInputModel
        {
            Id = optionToMove.Id,
            SortOrder = 1
        });

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(ConfigurationController.Index), redirect.ActionName);
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
    public async Task DeleteOptionRemovesOptionNormalizesSortOrderAndWritesAuditLog()
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

        var result = await controller.DeleteOption(option.Id);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(ConfigurationController.Index), redirect.ActionName);
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
    public async Task UpdateDisplayTimeZonePersistsSettingAndWritesAuditLog()
    {
        await using var fixture = await TestFixture.CreateAsync();
        using var controller = fixture.CreateConfigurationController();

        var result = await controller.UpdateDisplayTimeZone(new UpdateDisplayTimeZoneInputModel
        {
            TimeZoneId = "UTC"
        });

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(ConfigurationController.Index), redirect.ActionName);

        var setting = await fixture.DbContext.AppSettings.SingleAsync(x => x.Key == AppSettingKeys.DisplayTimeZone);
        var audit = await fixture.DbContext.AuditLogEntries.SingleAsync();

        Assert.Equal("UTC", setting.Value);
        Assert.Equal("Display time zone updated to 'UTC'.", controller.TempData["ConfigurationStatusMessage"]);
        Assert.Equal("UpdateDisplayTimeZone", audit.Action);
        Assert.Equal(nameof(AppSetting), audit.EntityType);
    }

    [Fact]
    public async Task UpdateDisplayTimeZoneRejectsUnknownTimeZone()
    {
        await using var fixture = await TestFixture.CreateAsync();
        using var controller = fixture.CreateConfigurationController();

        var result = await controller.UpdateDisplayTimeZone(new UpdateDisplayTimeZoneInputModel
        {
            TimeZoneId = "Not/AZone"
        });

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(ConfigurationController.Index), redirect.ActionName);
        Assert.Equal("The time zone 'Not/AZone' is not available on this server.", controller.TempData["ConfigurationError"]);
    }

    [Fact]
    public async Task SetRemoteSqlImportEnabledDisablesConfiguredImportAndWritesStatus()
    {
        await using var fixture = await TestFixture.CreateAsync();
        await fixture.DbContext.AppSettings.AddRangeAsync(
            new AppSetting { Key = AppSettingKeys.RemoteSqlImportServerName, Value = "sql.example.com" },
            new AppSetting { Key = AppSettingKeys.RemoteSqlImportDatabaseName, Value = "Herm" },
            new AppSetting { Key = AppSettingKeys.RemoteSqlImportIsEnabled, Value = true.ToString() });
        await fixture.DbContext.SaveChangesAsync();

        using var controller = fixture.CreateConfigurationController();
        var result = await controller.SetRemoteSqlImportEnabled(false);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(ConfigurationController.Index), redirect.ActionName);
        Assert.Equal(
            "Remote SQL import disabled. Scheduled and manual imports will be skipped until you enable it again.",
            controller.TempData["ConfigurationStatusMessage"]);
        Assert.Equal(
            false.ToString(),
            await fixture.DbContext.AppSettings.Where(x => x.Key == AppSettingKeys.RemoteSqlImportIsEnabled).Select(x => x.Value).SingleAsync());
        Assert.Contains(
            await fixture.DbContext.AuditLogEntries.Select(x => x.Action).ToListAsync(),
            action => string.Equals(action, "DisableImport", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RunRemoteSqlImportNowReturnsErrorWhenImportIsDisabled()
    {
        await using var fixture = await TestFixture.CreateAsync();
        await fixture.DbContext.AppSettings.AddRangeAsync(
            new AppSetting { Key = AppSettingKeys.RemoteSqlImportServerName, Value = "sql.example.com" },
            new AppSetting { Key = AppSettingKeys.RemoteSqlImportDatabaseName, Value = "Herm" },
            new AppSetting { Key = AppSettingKeys.RemoteSqlImportIsEnabled, Value = false.ToString() });
        await fixture.DbContext.SaveChangesAsync();

        using var controller = fixture.CreateConfigurationController();
        var result = await controller.RunRemoteSqlImportNow();

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(ConfigurationController.Index), redirect.ActionName);
        Assert.Equal("Remote SQL import is disabled. Enable it before running an import.", controller.TempData["ConfigurationError"]);
    }

    [Fact]
    public async Task ClearRemoteSqlImportConfigurationRemovesStoredSettingsAndCredentials()
    {
        await using var fixture = await TestFixture.CreateAsync();
        await fixture.DbContext.AppSettings.AddRangeAsync(
            new AppSetting { Key = AppSettingKeys.RemoteSqlImportServerName, Value = "sql.example.com" },
            new AppSetting { Key = AppSettingKeys.RemoteSqlImportDatabaseName, Value = "Herm" },
            new AppSetting { Key = AppSettingKeys.RemoteSqlImportIsEnabled, Value = false.ToString() },
            new AppSetting { Key = AppSettingKeys.RemoteSqlImportScheduleHours, Value = "6" },
            new AppSetting { Key = AppSettingKeys.RemoteSqlImportUserName, Value = "dp:user" },
            new AppSetting { Key = AppSettingKeys.RemoteSqlImportPassword, Value = "dp:password" },
            new AppSetting { Key = AppSettingKeys.RemoteSqlImportLastStatus, Value = "Failed" });
        await fixture.DbContext.SaveChangesAsync();

        using var controller = fixture.CreateConfigurationController();
        var result = await controller.ClearRemoteSqlImportConfiguration();

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(ConfigurationController.Index), redirect.ActionName);
        Assert.Equal("Remote SQL configuration was cleared from the database.", controller.TempData["ConfigurationStatusMessage"]);
        Assert.Empty(await fixture.DbContext.AppSettings.Where(x => x.Key.StartsWith("RemoteSqlImport.")).ToListAsync());
        Assert.Contains(
            await fixture.DbContext.AuditLogEntries.Select(x => x.Action).ToListAsync(),
            action => string.Equals(action, "ClearConfiguration", StringComparison.Ordinal));
    }

    [Fact]
    public async Task VerifyProductImportReturnsErrorReviewWhenFileExtensionIsInvalid()
    {
        await using var fixture = await TestFixture.CreateAsync();
        using var controller = fixture.CreateConfigurationController();
        var bytes = System.Text.Encoding.UTF8.GetBytes("not-a-csv");
        await using var stream = new MemoryStream(bytes);
        var file = new FormFile(stream, 0, bytes.Length, "file", "relationships.txt");

        var result = await controller.VerifyProductImport(file);

        var view = Assert.IsType<ViewResult>(result);
        Assert.Equal("Index", view.ViewName);
        var model = Assert.IsType<ConfigurationIndexViewModel>(view.Model);
        Assert.True(model.ProductImportReview.HasReview);
        Assert.Equal("relationships.txt", model.ProductImportReview.UploadedFileName);
        Assert.Equal("Only .csv files are supported for product import.", Assert.Single(model.ProductImportReview.Verification!.Errors));
    }

    [Fact]
    public async Task VerifyProductImportReturnsVerificationErrorsForInvalidCsvHeader()
    {
        await using var fixture = await TestFixture.CreateAsync();
        using var controller = fixture.CreateConfigurationController();
        var bytes = System.Text.Encoding.UTF8.GetBytes("wrong;header\nvalue");
        await using var stream = new MemoryStream(bytes);
        var file = new FormFile(stream, 0, bytes.Length, "file", "relationships.csv");

        var result = await controller.VerifyProductImport(file);

        var view = Assert.IsType<ViewResult>(result);
        Assert.Equal("Index", view.ViewName);
        var model = Assert.IsType<ConfigurationIndexViewModel>(view.Model);
        Assert.Equal("relationships.csv", model.ProductImportReview.UploadedFileName);
        Assert.Null(model.ProductImportReview.PendingImportToken);
        Assert.Equal("The CSV header must be 'MODEL;DOMAIN;CAPABILITY;COMPONENT;PRODUCT'.", Assert.Single(model.ProductImportReview.Verification!.Errors));
        Assert.Empty(Directory.GetFiles(Path.Combine(fixture.ContentRootPath, "App_Data", "PendingImports", "products"), "*.csv", SearchOption.TopDirectoryOnly));
    }

    [Fact]
    public async Task ImportVerifiedProductsWithMissingTokenRedirectsWithError()
    {
        await using var fixture = await TestFixture.CreateAsync();
        using var controller = fixture.CreateConfigurationController();

        var result = await controller.ImportVerifiedProducts("");

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(ConfigurationController.Index), redirect.ActionName);
        Assert.Equal("Verify a product CSV before importing it.", controller.TempData["ConfigurationError"]);
    }

    [Fact]
    public async Task ImportVerifiedProductsWithMissingFileRedirectsWithError()
    {
        await using var fixture = await TestFixture.CreateAsync();
        using var controller = fixture.CreateConfigurationController();

        var result = await controller.ImportVerifiedProducts("missing-token");

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(ConfigurationController.Index), redirect.ActionName);
        Assert.Equal("The verified product CSV is no longer available. Upload it again.", controller.TempData["ConfigurationError"]);
    }

    [Fact]
    public async Task ImportVerifiedProductsReturnsViewWhenCsvVerificationFails()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var pendingPath = Path.Combine(fixture.ContentRootPath, "App_Data", "PendingImports", "products", "bad-token.csv");
        Directory.CreateDirectory(Path.GetDirectoryName(pendingPath)!);
        await File.WriteAllTextAsync(pendingPath, "wrong;header\nvalue");
        using var controller = fixture.CreateConfigurationController();

        var result = await controller.ImportVerifiedProducts("bad-token");

        var view = Assert.IsType<ViewResult>(result);
        Assert.Equal("Index", view.ViewName);
        var model = Assert.IsType<ConfigurationIndexViewModel>(view.Model);
        Assert.Equal("The CSV header must be 'MODEL;DOMAIN;CAPABILITY;COMPONENT;PRODUCT'.", Assert.Single(model.ProductImportReview.Verification!.Errors));
        Assert.False(File.Exists(pendingPath));
    }

    [Fact]
    public async Task ImportVerifiedProductsImportsCsvAndWritesStatus()
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
        var result = await controller.ImportVerifiedProducts("good-token");

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(ConfigurationController.Index), redirect.ActionName);
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
    public async Task AbortProductImportDeletesPendingCsvAndWritesStatus()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var token = "product-token";
        var pendingPath = Path.Combine(fixture.ContentRootPath, "App_Data", "PendingImports", "products", token + ".csv");
        Directory.CreateDirectory(Path.GetDirectoryName(pendingPath)!);
        await File.WriteAllTextAsync(pendingPath, "pending");

        using var controller = fixture.CreateConfigurationController();
        var result = await controller.AbortProductImport(token);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(ConfigurationController.Index), redirect.ActionName);
        Assert.False(File.Exists(pendingPath));
        Assert.Equal("Product import was aborted.", controller.TempData["ConfigurationStatusMessage"]);
    }

    [Fact]
    public async Task AddOptionRejectsUnsupportedField()
    {
        await using var fixture = await TestFixture.CreateAsync();
        using var controller = fixture.CreateConfigurationController();

        var result = await controller.AddOption(new AddConfigurationOptionInputModel
        {
            FieldName = "UnknownField",
            Value = "Team Blue"
        });

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(ConfigurationController.Index), redirect.ActionName);
        Assert.Equal("That field is not supported.", controller.TempData["ConfigurationError"]);
    }

    [Fact]
    public async Task AddOptionRejectsBlankValue()
    {
        await using var fixture = await TestFixture.CreateAsync();
        using var controller = fixture.CreateConfigurationController();

        var result = await controller.AddOption(new AddConfigurationOptionInputModel
        {
            FieldName = ConfigurableFieldNames.Owner,
            Value = "   "
        });

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(ConfigurationController.Index), redirect.ActionName);
        Assert.Equal(ConfigurableFieldNames.Owner, Assert.IsType<string>(redirect.RouteValues!["expandedFieldName"]));
        Assert.Equal("Enter a value before saving.", controller.TempData["ConfigurationError"]);
    }

    [Fact]
    public async Task UpdateOptionOrderMissingOptionRedirectsWithoutChanges()
    {
        await using var fixture = await TestFixture.CreateAsync();
        using var controller = fixture.CreateConfigurationController();

        var result = await controller.UpdateOptionOrder(new UpdateConfigurationOptionOrderInputModel { Id = 999, SortOrder = 1 });

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(ConfigurationController.Index), redirect.ActionName);
    }

    [Fact]
    public async Task DeleteOptionMissingOptionRedirectsWithoutChanges()
    {
        await using var fixture = await TestFixture.CreateAsync();
        using var controller = fixture.CreateConfigurationController();

        var result = await controller.DeleteOption(999);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(ConfigurationController.Index), redirect.ActionName);
    }

    [Fact]
    public async Task UpdateDisplayTimeZoneRejectsBlankValue()
    {
        await using var fixture = await TestFixture.CreateAsync();
        using var controller = fixture.CreateConfigurationController();

        var result = await controller.UpdateDisplayTimeZone(new UpdateDisplayTimeZoneInputModel { TimeZoneId = "   " });

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(ConfigurationController.Index), redirect.ActionName);
        Assert.Equal("Choose a time zone before saving.", controller.TempData["ConfigurationError"]);
    }

    private sealed class TestFixture : IAsyncDisposable
    {
        private readonly SqliteConnection connection;
        private readonly TemporaryDirectory contentRoot;

        private TestFixture(SqliteConnection connection, TemporaryDirectory contentRoot, AppDbContext dbContext)
        {
            this.connection = connection;
            this.contentRoot = contentRoot;
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

        public async ValueTask DisposeAsync()
        {
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
}
