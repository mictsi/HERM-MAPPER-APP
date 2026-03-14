using HERMMapperApp.Controllers;
using HERMMapperApp.Data;
using HERMMapperApp.Models;
using HERMMapperApp.Services;
using HERMMapperApp.Tests.TestSupport;
using HERMMapperApp.ViewModels;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Xunit;

namespace HERMMapperApp.Tests.Controllers;

public sealed class ReferenceControllerTests
{
    [Fact]
    public async Task IndexAsyncReturnsBadRequestWhenModelStateInvalid()
    {
        await using var fixture = await TestFixture.CreateAsync();

        using var controller = fixture.CreateController();
        controller.ModelState.AddModelError("search", "Invalid");
        var result = await controller.IndexAsync(null, null, null);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task IndexAsyncBuildsFilteredCatalogueViewModel()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var (domainA, _, componentA) = await fixture.SeedComponentAsync("TD001", "Technology", "TP001", "Observability", "TC001", "Monitoring", isCustom: false);
        var (domainB, capabilityB, componentB) = await fixture.SeedComponentAsync("TD002", "Security", "TP002", "Identity", "CUST001", "Custom Gateway", isCustom: true);
        componentB.TechnologyComponentCode = "CUST001";
        await fixture.DbContext.SaveChangesAsync();

        using var controller = fixture.CreateController();
        controller.TempData["ImportStatusMessage"] = "Import complete";
        var result = await controller.IndexAsync("custom", domainB.Id, capabilityB.Id);

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<ReferenceCatalogueViewModel>(view.Model);
        Assert.Equal("custom", model.Search);
        Assert.Equal(domainB.Id, model.DomainId);
        Assert.Equal(capabilityB.Id, model.CapabilityId);
        Assert.Equal("Import complete", model.ImportStatusMessage);
        Assert.Single(model.Components);
        Assert.Equal(componentB.Id, model.Components[0].Id);
        Assert.Contains(model.Domains, domain => domain.Id == domainA.Id);
        Assert.DoesNotContain(model.Components, component => component.Id == componentA.Id);
    }

    [Fact]
    public async Task RestoreAsyncReturnsDeletedComponents()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var (_, _, activeComponent) = await fixture.SeedComponentAsync("TD001", "Technology", "TP001", "Observability", "TC001", "Monitoring", isCustom: false);
        var (_, _, deletedComponent) = await fixture.SeedComponentAsync("TD002", "Security", "TP002", "Identity", "TC002", "Identity", isCustom: false);
        deletedComponent.IsDeleted = true;
        deletedComponent.DeletedUtc = new DateTime(2026, 3, 4, 0, 0, 0, DateTimeKind.Utc);
        await fixture.DbContext.SaveChangesAsync();

        using var controller = fixture.CreateController();
        controller.TempData["ImportStatusMessage"] = "Ready";
        var result = await controller.RestoreAsync();

        var view = Assert.IsType<ViewResult>(result);
        Assert.Equal("Restore", view.ViewName);
        var model = Assert.IsType<ReferenceRestoreViewModel>(view.Model);
        Assert.Equal("Ready", model.StatusMessage);
        Assert.Single(model.Components);
        Assert.DoesNotContain(model.Components, component => component.Id == activeComponent.Id);
        Assert.Equal(deletedComponent.Id, model.Components[0].Id);
    }

    [Fact]
    public async Task VerifyImportAsyncRejectsMissingWorkbook()
    {
        await using var fixture = await TestFixture.CreateAsync();

        using var controller = fixture.CreateController();
        var result = await controller.VerifyImportAsync(null);

        var view = Assert.IsType<ViewResult>(result);
        Assert.Equal("Index", view.ViewName);
        var model = Assert.IsType<ReferenceCatalogueViewModel>(view.Model);
        Assert.Equal("Choose an .xlsx workbook before verifying the import.", Assert.Single(model.ImportReview.Verification!.Errors));
    }

    [Fact]
    public async Task VerifyImportAsyncRejectsNonExcelWorkbook()
    {
        await using var fixture = await TestFixture.CreateAsync();
        await using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("content"));
        var file = new FormFile(stream, 0, stream.Length, "file", "notes.txt");

        using var controller = fixture.CreateController();
        var result = await controller.VerifyImportAsync(file);

        var view = Assert.IsType<ViewResult>(result);
        Assert.Equal("Index", view.ViewName);
        var model = Assert.IsType<ReferenceCatalogueViewModel>(view.Model);
        Assert.Equal("notes.txt", model.ImportReview.UploadedFileName);
        Assert.Equal("Only Excel .xlsx workbooks are supported.", Assert.Single(model.ImportReview.Verification!.Errors));
    }

    [Fact]
    public async Task VerifyImportAsyncReturnsVerificationErrorsForInvalidWorkbookContent()
    {
        await using var fixture = await TestFixture.CreateAsync();
        await using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("not-a-valid-zip-workbook"));
        var file = new FormFile(stream, 0, stream.Length, "file", "catalogue.xlsx");

        using var controller = fixture.CreateController();
        var result = await controller.VerifyImportAsync(file);

        var view = Assert.IsType<ViewResult>(result);
        Assert.Equal("Index", view.ViewName);
        var model = Assert.IsType<ReferenceCatalogueViewModel>(view.Model);
        Assert.Equal("catalogue.xlsx", model.ImportReview.UploadedFileName);
        Assert.Null(model.ImportReview.PendingImportToken);
        Assert.NotEmpty(model.ImportReview.Verification!.Errors);
        Assert.Empty(Directory.GetFiles(Path.Combine(fixture.ContentRootPath, "App_Data", "PendingImports"), "*.xlsx", SearchOption.TopDirectoryOnly));
        Assert.Single(await fixture.DbContext.AuditLogEntries.ToListAsync());
    }

    [Fact]
    public async Task VerifyImportAsyncReturnsBadRequestWhenModelStateInvalid()
    {
        await using var fixture = await TestFixture.CreateAsync();

        using var controller = fixture.CreateController();
        controller.ModelState.AddModelError("workbook", "Invalid");
        var result = await controller.VerifyImportAsync(null);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task ImportVerifiedAsyncRedirectsWhenTokenMissing()
    {
        await using var fixture = await TestFixture.CreateAsync();

        using var controller = fixture.CreateController();
        var result = await controller.ImportVerifiedAsync(string.Empty);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Index", redirect.ActionName);
        Assert.Equal("Verify a workbook before importing it.", controller.TempData["ImportStatusMessage"]);
    }

    [Fact]
    public async Task ImportVerifiedAsyncRedirectsWhenPendingFileMissing()
    {
        await using var fixture = await TestFixture.CreateAsync();

        using var controller = fixture.CreateController();
        var result = await controller.ImportVerifiedAsync("missing-token");

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Index", redirect.ActionName);
        Assert.Equal("The verified workbook is no longer available. Upload it again.", controller.TempData["ImportStatusMessage"]);
    }

    [Fact]
    public async Task ImportVerifiedAsyncReturnsViewWhenWorkbookVerificationFails()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var pendingDirectory = Path.Combine(fixture.ContentRootPath, "App_Data", "PendingImports");
        Directory.CreateDirectory(pendingDirectory);
        var pendingPath = Path.Combine(pendingDirectory, "invalid-token.xlsx");
        await File.WriteAllTextAsync(pendingPath, "not-a-valid-zip-workbook");

        using var controller = fixture.CreateController();
        var result = await controller.ImportVerifiedAsync("invalid-token");

        var view = Assert.IsType<ViewResult>(result);
        Assert.Equal("Index", view.ViewName);
        var model = Assert.IsType<ReferenceCatalogueViewModel>(view.Model);
        Assert.NotEmpty(model.ImportReview.Verification!.Errors);
        Assert.False(File.Exists(pendingPath));
    }

    [Fact]
    public async Task ImportVerifiedAsyncImportsWorkbookAndRedirects()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var pendingPath = Path.Combine(fixture.ContentRootPath, "App_Data", "PendingImports", "valid-token.xlsx");
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

        using var controller = fixture.CreateController();
        var result = await controller.ImportVerifiedAsync("valid-token");

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Index", redirect.ActionName);
        Assert.False(File.Exists(pendingPath));
        Assert.Equal(
            "TRM model imported. Domains +1/0 updated, capabilities +1/0 updated, components +1/0 updated.",
            controller.TempData["ImportStatusMessage"]);
        Assert.Equal(1, await fixture.DbContext.TrmDomains.CountAsync());
        Assert.Equal(1, await fixture.DbContext.TrmCapabilities.CountAsync());
        Assert.Equal(1, await fixture.DbContext.TrmComponents.CountAsync());
    }

    [Fact]
    public async Task ImportVerifiedAsyncReturnsBadRequestWhenModelStateInvalid()
    {
        await using var fixture = await TestFixture.CreateAsync();

        using var controller = fixture.CreateController();
        controller.ModelState.AddModelError("pendingImportToken", "Invalid");
        var result = await controller.ImportVerifiedAsync("token");

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task DeleteComponentAsyncMarksComponentDeletedAndWritesHistory()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var (_, _, component) = await fixture.SeedComponentAsync("TD001", "Technology", "TP001", "Observability", "TC001", "Monitoring", isCustom: false);

        using var controller = fixture.CreateController();
        var result = await controller.DeleteComponentAsync(component.Id);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Index", redirect.ActionName);

        var updatedComponent = await fixture.DbContext.TrmComponents.SingleAsync();
        Assert.True(updatedComponent.IsDeleted);
        Assert.NotNull(updatedComponent.DeletedUtc);
        Assert.Equal("Moved component TC001 Monitoring to trash.", controller.TempData["ImportStatusMessage"]);
        Assert.Equal("Deleted", (await fixture.DbContext.TrmComponentVersions.SingleAsync()).ChangeType);
        Assert.Equal("Delete", (await fixture.DbContext.AuditLogEntries.SingleAsync()).Action);
    }

    [Fact]
    public async Task DeleteComponentAsyncReturnsNotFoundWhenComponentMissing()
    {
        await using var fixture = await TestFixture.CreateAsync();

        using var controller = fixture.CreateController();
        var result = await controller.DeleteComponentAsync(999);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task DeleteComponentAsyncReturnsBadRequestWhenModelStateInvalid()
    {
        await using var fixture = await TestFixture.CreateAsync();

        using var controller = fixture.CreateController();
        controller.ModelState.AddModelError("id", "Invalid");
        var result = await controller.DeleteComponentAsync(1);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task RestoreComponentAsyncRestoresDeletedComponent()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var (_, _, component) = await fixture.SeedComponentAsync("TD001", "Technology", "TP001", "Observability", "TC001", "Monitoring", isCustom: false);
        component.IsDeleted = true;
        component.DeletedUtc = DateTime.UtcNow.AddDays(-1);
        component.DeletedReason = "deleted";
        await fixture.DbContext.SaveChangesAsync();

        using var controller = fixture.CreateController();
        var result = await controller.RestoreComponentAsync(component.Id);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Restore", redirect.ActionName);

        var updatedComponent = await fixture.DbContext.TrmComponents.SingleAsync();
        Assert.False(updatedComponent.IsDeleted);
        Assert.Null(updatedComponent.DeletedUtc);
        Assert.Null(updatedComponent.DeletedReason);
        Assert.Equal("Restored", (await fixture.DbContext.TrmComponentVersions.SingleAsync()).ChangeType);
    }

    [Fact]
    public async Task RestoreComponentAsyncReturnsNotFoundWhenComponentMissing()
    {
        await using var fixture = await TestFixture.CreateAsync();

        using var controller = fixture.CreateController();
        var result = await controller.RestoreComponentAsync(999);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task RestoreComponentAsyncReturnsBadRequestWhenModelStateInvalid()
    {
        await using var fixture = await TestFixture.CreateAsync();

        using var controller = fixture.CreateController();
        controller.ModelState.AddModelError("id", "Invalid");
        var result = await controller.RestoreComponentAsync(1);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task PermanentlyDeleteComponentAsyncRemovesDeletedComponent()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var (_, _, component) = await fixture.SeedComponentAsync("TD001", "Technology", "TP001", "Observability", "TC001", "Monitoring", isCustom: false);
        component.IsDeleted = true;
        component.DeletedUtc = DateTime.UtcNow.AddDays(-1);
        await fixture.DbContext.SaveChangesAsync();

        using var controller = fixture.CreateController();
        var result = await controller.PermanentlyDeleteComponentAsync(component.Id);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Restore", redirect.ActionName);
        Assert.Equal(0, await fixture.DbContext.TrmComponents.CountAsync());
        Assert.Equal("PermanentDelete", (await fixture.DbContext.AuditLogEntries.SingleAsync()).Action);
    }

    [Fact]
    public async Task PermanentlyDeleteComponentAsyncReturnsBadRequestWhenModelStateInvalid()
    {
        await using var fixture = await TestFixture.CreateAsync();

        using var controller = fixture.CreateController();
        controller.ModelState.AddModelError("id", "Invalid");
        var result = await controller.PermanentlyDeleteComponentAsync(1);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task PermanentlyDeleteComponentAsyncReturnsNotFoundWhenDeletedComponentMissing()
    {
        await using var fixture = await TestFixture.CreateAsync();

        using var controller = fixture.CreateController();
        var result = await controller.PermanentlyDeleteComponentAsync(999);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task HistoryAsyncReturnsComponentVersions()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var (_, _, component) = await fixture.SeedComponentAsync("TD001", "Technology", "TP001", "Observability", "TC001", "Monitoring", isCustom: false);
        await fixture.DbContext.TrmComponentVersions.AddRangeAsync(
            new TrmComponentVersion
            {
                TrmComponentId = component.Id,
                VersionNumber = 1,
                ChangeType = "Created",
                Name = component.Name,
                ModelCode = component.Code,
                ChangedUtc = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc)
            },
            new TrmComponentVersion
            {
                TrmComponentId = component.Id,
                VersionNumber = 2,
                ChangeType = "Updated",
                Name = component.Name,
                ModelCode = component.Code,
                ChangedUtc = new DateTime(2026, 3, 2, 0, 0, 0, DateTimeKind.Utc)
            });
        await fixture.DbContext.SaveChangesAsync();

        using var controller = fixture.CreateController();
        var result = await controller.HistoryAsync(component.Id);

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<ComponentHistoryViewModel>(view.Model);
        Assert.Equal(component.Id, model.Component.Id);
        Assert.Equal([2, 1], model.Versions.Select(version => version.VersionNumber).ToArray());
    }

    [Fact]
    public async Task HistoryAsyncReturnsNotFoundWhenComponentMissing()
    {
        await using var fixture = await TestFixture.CreateAsync();

        using var controller = fixture.CreateController();
        var result = await controller.HistoryAsync(999);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task HistoryAsyncReturnsBadRequestWhenModelStateInvalid()
    {
        await using var fixture = await TestFixture.CreateAsync();

        using var controller = fixture.CreateController();
        controller.ModelState.AddModelError("id", "Invalid");
        var result = await controller.HistoryAsync(1);

        Assert.IsType<BadRequestObjectResult>(result);
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

        public async Task<(TrmDomain Domain, TrmCapability Capability, TrmComponent Component)> SeedComponentAsync(
            string domainCode,
            string domainName,
            string capabilityCode,
            string capabilityName,
            string componentCode,
            string componentName,
            bool isCustom)
        {
            var domain = new TrmDomain { Code = domainCode, Name = domainName };
            var capability = new TrmCapability { Code = capabilityCode, Name = capabilityName, ParentDomain = domain, ParentDomainCode = domain.Code };
            var component = new TrmComponent
            {
                Code = componentCode,
                Name = componentName,
                ParentCapability = capability,
                ParentCapabilityCode = capability.Code,
                IsCustom = isCustom
            };
            component.CapabilityLinks.Add(new TrmComponentCapabilityLink { TrmComponent = component, TrmCapability = capability });

            await DbContext.AddRangeAsync(domain, capability, component);
            await DbContext.SaveChangesAsync();
            return (domain, capability, component);
        }

        public ReferenceController CreateController()
        {
            var controller = new ReferenceController(
                DbContext,
                new TrmWorkbookImportService(DbContext, new ComponentVersioningService(DbContext), new AuditLogService(DbContext)),
                new ComponentVersioningService(DbContext),
                new AuditLogService(DbContext),
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
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"herm-mapper-reference-tests-{Guid.NewGuid():N}");
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