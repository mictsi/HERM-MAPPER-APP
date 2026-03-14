using HERMMapperApp.Controllers;
using HERMMapperApp.Data;
using HERMMapperApp.Models;
using HERMMapperApp.Services;
using HERMMapperApp.ViewModels;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HERMMapperApp.Tests.Controllers;

public sealed class ServicesControllerTests
{
    [Fact]
    public async Task CreatePostPersistsServiceNormalizesSelectionsAndWritesAudit()
    {
        await using var fixture = await TestFixture.CreateAsync();
        await fixture.SeedConfigurableOptionsAsync();
        using var controller = fixture.CreateController();

        var result = await controller.Create(new ServiceEditViewModel
        {
            Name = "Security Operations",
            Description = "SOC tooling",
            Owner = " Team Blue ",
            LifecycleStatus = " Production "
        });

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(ServicesController.Connections), redirect.ActionName);

        var service = await fixture.DbContext.ServiceCatalogItems.SingleAsync();
        var audit = await fixture.DbContext.AuditLogEntries.SingleAsync();

        Assert.Equal("Security Operations", service.Name);
        Assert.Equal("Team Blue", service.Owner);
        Assert.Equal("Production", service.LifecycleStatus);
        Assert.Equal(service.Id, redirect.RouteValues!["id"]);
        Assert.Equal("Create", audit.Action);
    }

    [Fact]
    public async Task ConnectionsPostPersistsBranchingGraphAndWritesAudit()
    {
        await using var fixture = await TestFixture.CreateAsync();
        await fixture.SeedConfigurableOptionsAsync();

        var products = await fixture.SeedProductsAsync("Entry", "API", "Broker", "Portal");
        fixture.DbContext.ServiceCatalogItems.Add(new ServiceCatalogItem
        {
            Name = "Loopback Flow",
            Owner = "Team Blue",
            LifecycleStatus = "Production"
        });
        await fixture.DbContext.SaveChangesAsync();

        var serviceId = await fixture.DbContext.ServiceCatalogItems.Select(x => x.Id).SingleAsync();
        using var controller = fixture.CreateController();
        var canvasStateJson = JsonSerializer.Serialize(
            new
            {
                nodes = new[]
                {
                    new { productId = products[0].Id, x = 120, y = 80 },
                    new { productId = products[1].Id, x = 380, y = 80 },
                    new { productId = products[2].Id, x = 380, y = 280 },
                    new { productId = products[3].Id, x = 650, y = 280 }
                },
                connections = new[]
                {
                    new { fromProductId = products[0].Id, toProductId = products[1].Id },
                    new { fromProductId = products[0].Id, toProductId = products[2].Id },
                    new { fromProductId = products[2].Id, toProductId = products[3].Id }
                }
            },
            new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

        var result = await controller.Connections(serviceId, new ServiceConnectionEditorViewModel
        {
            CanvasStateJson = canvasStateJson
        });

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(ServicesController.Connections), redirect.ActionName);

        var service = await fixture.DbContext.ServiceCatalogItems
            .Include(x => x.ProductLinks)
            .ThenInclude(x => x.ProductCatalogItem)
            .Include(x => x.ProductConnections)
            .SingleAsync();
        var audit = await fixture.DbContext.AuditLogEntries.SingleAsync();

        Assert.Equal(
            ["Entry", "API", "Broker", "Portal"],
            service.GetOrderedProductLinks().Select(x => x.ProductCatalogItem.Name).ToArray());
        Assert.Equal(3, service.ProductConnections.Count);
        Assert.NotNull(service.ConnectionLayoutJson);
        using (var document = JsonDocument.Parse(service.ConnectionLayoutJson!))
        {
            Assert.Equal(4, document.RootElement.GetProperty("nodes").GetArrayLength());
            Assert.Equal(3, document.RootElement.GetProperty("connections").GetArrayLength());
        }

        Assert.Equal("UpdateConnections", audit.Action);
    }

    [Fact]
    public async Task EditPostUpdatesServiceMetadataAndWritesAudit()
    {
        await using var fixture = await TestFixture.CreateAsync();
        await fixture.SeedConfigurableOptionsAsync();

        var service = new ServiceCatalogItem
        {
            Name = "Payments",
            Description = "Existing flow",
            Owner = "Team Blue",
            LifecycleStatus = "Trial"
        };

        fixture.DbContext.ServiceCatalogItems.Add(service);
        await fixture.DbContext.SaveChangesAsync();

        using var controller = fixture.CreateController();
        var result = await controller.Edit(service.Id, new ServiceEditViewModel
        {
            Name = "Payments Revised",
            Description = "Updated flow",
            Owner = " Team Green ",
            LifecycleStatus = " Production "
        });

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(ServicesController.Connections), redirect.ActionName);

        var updatedService = await fixture.DbContext.ServiceCatalogItems.SingleAsync(x => x.Id == service.Id);
        var audit = await fixture.DbContext.AuditLogEntries.SingleAsync();

        Assert.Equal("Payments Revised", updatedService.Name);
        Assert.Equal("Team Green", updatedService.Owner);
        Assert.Equal("Production", updatedService.LifecycleStatus);
        Assert.Equal("Update", audit.Action);
    }

    [Fact]
    public async Task ConnectionsGetBuildsRowsFromLegacyLinearFlow()
    {
        await using var fixture = await TestFixture.CreateAsync();

        var products = await fixture.SeedProductsAsync("Portal", "API", "Queue");
        fixture.DbContext.ServiceCatalogItems.Add(new ServiceCatalogItem
        {
            Name = "Customer Journey",
            Owner = "Team Blue",
            LifecycleStatus = "Production",
            ProductLinks =
            [
                new ServiceCatalogItemProduct { ProductCatalogItemId = products[0].Id, SortOrder = 1 },
                new ServiceCatalogItemProduct { ProductCatalogItemId = products[1].Id, SortOrder = 2 },
                new ServiceCatalogItemProduct { ProductCatalogItemId = products[2].Id, SortOrder = 3 }
            ]
        });
        await fixture.DbContext.SaveChangesAsync();

        var serviceId = await fixture.DbContext.ServiceCatalogItems.Select(x => x.Id).SingleAsync();
        var result = await fixture.CreateController().Connections(serviceId);

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<ServiceConnectionEditorViewModel>(view.Model);

        Assert.True(model.UsesLegacyFlow);
        Assert.Collection(
            model.ConnectionRows,
            row =>
            {
                Assert.Equal(products[0].Id, row.FromProductId);
                Assert.Equal(products[1].Id, row.ToProductId);
            },
            row =>
            {
                Assert.Equal(products[1].Id, row.FromProductId);
                Assert.Equal(products[2].Id, row.ToProductId);
            });
    }

    [Fact]
    public async Task IndexFiltersBySearchOwnerAndLifecycleStatus()
    {
        await using var fixture = await TestFixture.CreateAsync();
        await fixture.SeedConfigurableOptionsAsync();

        var products = await fixture.SeedProductsAsync("Gateway", "Ledger", "Observability");
        fixture.DbContext.ServiceCatalogItems.AddRange(
            new ServiceCatalogItem
            {
                Name = "Service A",
                Owner = "Team Blue",
                LifecycleStatus = "Production",
                ProductLinks =
                [
                    new ServiceCatalogItemProduct { ProductCatalogItemId = products[0].Id, SortOrder = 1 },
                    new ServiceCatalogItemProduct { ProductCatalogItemId = products[1].Id, SortOrder = 2 }
                ]
            },
            new ServiceCatalogItem
            {
                Name = "Service B",
                Owner = "Team Red",
                LifecycleStatus = "Trial",
                ProductLinks =
                [
                    new ServiceCatalogItemProduct { ProductCatalogItemId = products[2].Id, SortOrder = 1 },
                    new ServiceCatalogItemProduct { ProductCatalogItemId = products[1].Id, SortOrder = 2 }
                ]
            });
        await fixture.DbContext.SaveChangesAsync();

        var result = await fixture.CreateController().Index("gateway", "Team Blue", "Production", ServiceSortOptions.NameDesc);

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<ServicesIndexViewModel>(view.Model);

        Assert.Equal("Team Blue", model.Owner);
        Assert.Equal("Production", model.LifecycleStatus);
        Assert.Equal(ServiceSortOptions.NameDesc, model.Sort);
        Assert.Single(model.Services);
        Assert.Equal("Service A", model.Services[0].Name);
    }

    [Fact]
    public async Task IndexExcludesDeletedServices()
    {
        await using var fixture = await TestFixture.CreateAsync();
        await fixture.SeedConfigurableOptionsAsync();

        fixture.DbContext.ServiceCatalogItems.AddRange(
            new ServiceCatalogItem
            {
                Name = "Visible Service",
                Owner = "Team Blue",
                LifecycleStatus = "Production"
            },
            new ServiceCatalogItem
            {
                Name = "Deleted Service",
                Owner = "Team Blue",
                LifecycleStatus = "Production",
                IsDeleted = true,
                DeletedUtc = DateTime.UtcNow,
                DeletedReason = "Moved to trash from the service catalogue."
            });
        await fixture.DbContext.SaveChangesAsync();

        var result = await fixture.CreateController().Index(null, null, null, null);

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<ServicesIndexViewModel>(view.Model);
        Assert.Collection(model.Services, service => Assert.Equal("Visible Service", service.Name));
    }

    [Fact]
    public async Task VisualizeReturnsGraphConnectionsInSavedOrder()
    {
        await using var fixture = await TestFixture.CreateAsync();

        var products = await fixture.SeedProductsAsync("Portal", "API", "Queue", "Dashboard");
        var service = new ServiceCatalogItem
        {
            Name = "Customer Journey",
            Owner = "Team Blue",
            LifecycleStatus = "Production",
            ProductConnections =
            [
                new ServiceCatalogItemConnection { FromProductCatalogItemId = products[0].Id, ToProductCatalogItemId = products[1].Id, SortOrder = 1 },
                new ServiceCatalogItemConnection { FromProductCatalogItemId = products[0].Id, ToProductCatalogItemId = products[2].Id, SortOrder = 2 },
                new ServiceCatalogItemConnection { FromProductCatalogItemId = products[2].Id, ToProductCatalogItemId = products[3].Id, SortOrder = 3 }
            ],
            ProductLinks =
            [
                new ServiceCatalogItemProduct { ProductCatalogItemId = products[0].Id, SortOrder = 1 },
                new ServiceCatalogItemProduct { ProductCatalogItemId = products[1].Id, SortOrder = 2 },
                new ServiceCatalogItemProduct { ProductCatalogItemId = products[2].Id, SortOrder = 3 },
                new ServiceCatalogItemProduct { ProductCatalogItemId = products[3].Id, SortOrder = 4 }
            ]
        };

        fixture.DbContext.ServiceCatalogItems.Add(service);
        await fixture.DbContext.SaveChangesAsync();

        var result = await fixture.CreateController().Visualize(service.Id);

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<ServiceVisualizationViewModel>(view.Model);

        Assert.True(model.UsesGraphConnections);
        Assert.Equal(["Portal", "API", "Queue", "Dashboard"], model.ProductNames.ToArray());
        Assert.Equal(3, model.Connections.Count);
        Assert.Equal("Portal", model.Connections[0].FromProductName);
        Assert.Equal("API", model.Connections[0].ToProductName);
        Assert.Equal("Portal", model.Connections[1].FromProductName);
        Assert.Equal("Queue", model.Connections[1].ToProductName);
        Assert.Equal("Queue", model.Connections[2].FromProductName);
        Assert.Equal("Dashboard", model.Connections[2].ToProductName);
    }

    [Fact]
    public async Task DeleteConfirmedSoftDeletesServiceAndWritesAudit()
    {
        await using var fixture = await TestFixture.CreateAsync();

        fixture.DbContext.ServiceCatalogItems.Add(new ServiceCatalogItem
        {
            Name = "Legacy Service",
            Owner = "Team Blue",
            LifecycleStatus = "Production"
        });
        await fixture.DbContext.SaveChangesAsync();

        var serviceId = await fixture.DbContext.ServiceCatalogItems.Select(x => x.Id).SingleAsync();
        var result = await fixture.CreateController().DeleteConfirmed(serviceId);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(ServicesController.Index), redirect.ActionName);
        var deletedService = await fixture.DbContext.ServiceCatalogItems.SingleAsync();
        Assert.True(deletedService.IsDeleted);
        Assert.NotNull(deletedService.DeletedUtc);
        Assert.Equal("Moved to trash from the service catalogue.", deletedService.DeletedReason);
        Assert.Equal("Delete", (await fixture.DbContext.AuditLogEntries.SingleAsync()).Action);
    }

    [Fact]
    public async Task RestoreDeletedClearsSoftDeleteStateAndWritesAudit()
    {
        await using var fixture = await TestFixture.CreateAsync();

        fixture.DbContext.ServiceCatalogItems.Add(new ServiceCatalogItem
        {
            Name = "Legacy Service",
            Owner = "Team Blue",
            LifecycleStatus = "Production",
            IsDeleted = true,
            DeletedUtc = DateTime.UtcNow.AddDays(-1),
            DeletedReason = "Moved to trash from the service catalogue."
        });
        await fixture.DbContext.SaveChangesAsync();

        var serviceId = await fixture.DbContext.ServiceCatalogItems.Select(x => x.Id).SingleAsync();
        var result = await fixture.CreateController().RestoreDeleted(serviceId);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(ServicesController.Restore), redirect.ActionName);

        var restoredService = await fixture.DbContext.ServiceCatalogItems.SingleAsync();
        Assert.False(restoredService.IsDeleted);
        Assert.Null(restoredService.DeletedUtc);
        Assert.Null(restoredService.DeletedReason);
        Assert.Equal("Restore", (await fixture.DbContext.AuditLogEntries.SingleAsync()).Action);
    }

    [Fact]
    public async Task PermanentDeleteRemovesDeletedServiceAndWritesAudit()
    {
        await using var fixture = await TestFixture.CreateAsync();

        fixture.DbContext.ServiceCatalogItems.Add(new ServiceCatalogItem
        {
            Name = "Legacy Service",
            Owner = "Team Blue",
            LifecycleStatus = "Production",
            IsDeleted = true,
            DeletedUtc = DateTime.UtcNow.AddDays(-1),
            DeletedReason = "Moved to trash from the service catalogue."
        });
        await fixture.DbContext.SaveChangesAsync();

        var serviceId = await fixture.DbContext.ServiceCatalogItems.Select(x => x.Id).SingleAsync();
        var result = await fixture.CreateController().PermanentDelete(serviceId);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(ServicesController.Restore), redirect.ActionName);
        Assert.Empty(await fixture.DbContext.ServiceCatalogItems.ToListAsync());
        Assert.Equal("PermanentDelete", (await fixture.DbContext.AuditLogEntries.SingleAsync()).Action);
    }

    private sealed class TestFixture : IAsyncDisposable
    {
        private readonly SqliteConnection connection;

        private TestFixture(SqliteConnection connection, AppDbContext dbContext)
        {
            this.connection = connection;
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

            return new TestFixture(connection, dbContext);
        }

        public ServicesController CreateController()
        {
            var controller = new ServicesController(
                DbContext,
                new AuditLogService(DbContext),
                new ConfigurableFieldService(DbContext));

            controller.TempData = new TempDataDictionary(new DefaultHttpContext(), new TestTempDataProvider());
            return controller;
        }

        public async Task SeedConfigurableOptionsAsync()
        {
            DbContext.ConfigurableFieldOptions.AddRange(
                new ConfigurableFieldOption { FieldName = ConfigurableFieldNames.Owner, Value = "Team Blue", SortOrder = 1 },
                new ConfigurableFieldOption { FieldName = ConfigurableFieldNames.Owner, Value = "Team Red", SortOrder = 2 },
                new ConfigurableFieldOption { FieldName = ConfigurableFieldNames.Owner, Value = "Team Green", SortOrder = 3 },
                new ConfigurableFieldOption { FieldName = ConfigurableFieldNames.LifecycleStatus, Value = "Production", SortOrder = 1 },
                new ConfigurableFieldOption { FieldName = ConfigurableFieldNames.LifecycleStatus, Value = "Trial", SortOrder = 2 });
            await DbContext.SaveChangesAsync();
        }

        public async Task<List<ProductCatalogItem>> SeedProductsAsync(params string[] names)
        {
            var products = names
                .Select(name => new ProductCatalogItem { Name = name })
                .ToList();

            DbContext.ProductCatalogItems.AddRange(products);
            await DbContext.SaveChangesAsync();
            return products;
        }

        public async ValueTask DisposeAsync()
        {
            await DbContext.DisposeAsync();
            await connection.DisposeAsync();
        }
    }

    private sealed class TestTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();

        public void SaveTempData(HttpContext context, IDictionary<string, object> values)
        {
        }
    }
}
