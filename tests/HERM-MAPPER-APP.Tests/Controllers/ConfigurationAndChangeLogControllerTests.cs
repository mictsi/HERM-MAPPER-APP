using HERM_MAPPER_APP.Controllers;
using HERM_MAPPER_APP.Data;
using HERM_MAPPER_APP.Models;
using HERM_MAPPER_APP.Services;
using HERM_MAPPER_APP.ViewModels;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Xunit;

namespace HERM_MAPPER_APP.Tests.Controllers;

public sealed class ConfigurationAndChangeLogControllerTests
{
    [Fact]
    public async Task ChangeLogIndex_FiltersBySearch_AndOrdersNewestFirst()
    {
        await using var fixture = await TestFixture.CreateAsync();
        fixture.DbContext.AuditLogEntries.AddRange(
            new AuditLogEntry
            {
                Category = "Configuration",
                Action = "Import",
                EntityType = "TrmWorkbook",
                Summary = "Imported workbook",
                Details = "Workbook import passed",
                OccurredUtc = new DateTime(2026, 3, 1, 9, 0, 0, DateTimeKind.Utc)
            },
            new AuditLogEntry
            {
                Category = "Product",
                Action = "Create",
                EntityType = nameof(ProductCatalogItem),
                Summary = "Created Sentinel",
                OccurredUtc = new DateTime(2026, 3, 3, 9, 0, 0, DateTimeKind.Utc)
            },
            new AuditLogEntry
            {
                Category = "Configuration",
                Action = "VerifyProductImport",
                EntityType = nameof(ProductCatalogItem),
                Summary = "Verified CSV",
                Details = "Rows read: 1",
                OccurredUtc = new DateTime(2026, 3, 4, 9, 0, 0, DateTimeKind.Utc)
            });
        await fixture.DbContext.SaveChangesAsync();

        var result = await fixture.CreateChangeLogController().Index("Product");

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<ChangeLogIndexViewModel>(view.Model);

        Assert.Equal("Product", model.Search);
        Assert.Equal(2, model.Entries.Count);
        Assert.Equal("VerifyProductImport", model.Entries[0].Action);
        Assert.Equal("Create", model.Entries[1].Action);
    }

    [Fact]
    public async Task AddOption_CreatesOption_AndWritesAuditLog()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var controller = fixture.CreateConfigurationController();

        var result = await controller.AddOption(new AddConfigurationOptionInputModel
        {
            FieldName = " Owner ",
            Value = " Team Blue "
        });

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(ConfigurationController.Index), redirect.ActionName);

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
    public async Task AddOption_RejectsDuplicateValue_IgnoringCase()
    {
        await using var fixture = await TestFixture.CreateAsync();
        fixture.DbContext.ConfigurableFieldOptions.Add(new ConfigurableFieldOption
        {
            FieldName = ConfigurableFieldNames.Owner,
            Value = "Team Blue",
            SortOrder = 1
        });
        await fixture.DbContext.SaveChangesAsync();

        var controller = fixture.CreateConfigurationController();

        var result = await controller.AddOption(new AddConfigurationOptionInputModel
        {
            FieldName = ConfigurableFieldNames.Owner,
            Value = "team blue"
        });

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(ConfigurationController.Index), redirect.ActionName);
        Assert.Equal(1, await fixture.DbContext.ConfigurableFieldOptions.CountAsync());
        Assert.Equal("Owner value 'team blue' already exists.", controller.TempData["ConfigurationError"]);
    }

    [Fact]
    public async Task UpdateOptionOrder_ReordersOptions_AndRenumbersSequentially()
    {
        await using var fixture = await TestFixture.CreateAsync();
        fixture.DbContext.ConfigurableFieldOptions.AddRange(
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
        var controller = fixture.CreateConfigurationController();

        var result = await controller.UpdateOptionOrder(new UpdateConfigurationOptionOrderInputModel
        {
            Id = optionToMove.Id,
            SortOrder = 1
        });

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(ConfigurationController.Index), redirect.ActionName);

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
    public async Task DeleteOption_RemovesOption_NormalizesSortOrder_AndWritesAuditLog()
    {
        await using var fixture = await TestFixture.CreateAsync();
        fixture.DbContext.ConfigurableFieldOptions.AddRange(
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
        var controller = fixture.CreateConfigurationController();

        var result = await controller.DeleteOption(option.Id);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(ConfigurationController.Index), redirect.ActionName);

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
    public async Task VerifyProductImport_ReturnsErrorReview_WhenFileExtensionIsInvalid()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var controller = fixture.CreateConfigurationController();
        var file = CreateFormFile("relationships.txt", "not-a-csv");

        var result = await controller.VerifyProductImport(file);

        var view = Assert.IsType<ViewResult>(result);
        Assert.Equal("Index", view.ViewName);
        var model = Assert.IsType<ConfigurationIndexViewModel>(view.Model);
        Assert.True(model.ProductImportReview.HasReview);
        Assert.Equal("relationships.txt", model.ProductImportReview.UploadedFileName);
        Assert.Equal("Only .csv files are supported for product import.", Assert.Single(model.ProductImportReview.Verification!.Errors));
    }

    [Fact]
    public async Task ImportVerifiedProducts_WithMissingToken_RedirectsWithError()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var controller = fixture.CreateConfigurationController();

        var result = await controller.ImportVerifiedProducts("");

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(ConfigurationController.Index), redirect.ActionName);
        Assert.Equal("Verify a product CSV before importing it.", controller.TempData["ConfigurationError"]);
    }

    private static IFormFile CreateFormFile(string fileName, string content)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(content);
        var stream = new MemoryStream(bytes);
        return new FormFile(stream, 0, bytes.Length, "file", fileName);
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

        public static async Task<TestFixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();

            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(connection)
                .Options;

            var dbContext = new AppDbContext(options);
            await dbContext.Database.EnsureCreatedAsync();

            return new TestFixture(connection, new TemporaryDirectory(), dbContext);
        }

        public ChangeLogController CreateChangeLogController() => new(DbContext);

        public ConfigurationController CreateConfigurationController()
        {
            var controller = new ConfigurationController(
                DbContext,
                new ConfigurableFieldService(DbContext),
                new AuditLogService(DbContext),
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
