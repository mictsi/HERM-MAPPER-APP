using HERMMapperApp.Controllers;
using HERMMapperApp.Data;
using HERMMapperApp.Models;
using HERMMapperApp.Services;
using HERMMapperApp.ViewModels;
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

        var products = await fixture.SeedProductsAsync("Sentinel", "Purview");
        using var controller = fixture.CreateController();

        var result = await controller.Create(new ServiceEditViewModel
        {
            Name = "Security Operations",
            Description = "SOC tooling",
            Owner = " Team Blue ",
            LifecycleStatus = " Production ",
            ProductRows =
            [
                new ServiceProductRowViewModel { ProductId = products[0].Id },
                new ServiceProductRowViewModel { ProductId = products[1].Id }
            ]
        });

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(ServicesController.Index), redirect.ActionName);

        var service = await fixture.DbContext.ServiceCatalogItems
            .Include(x => x.ProductLinks)
            .ThenInclude(x => x.ProductCatalogItem)
            .SingleAsync();
        var audit = await fixture.DbContext.AuditLogEntries.SingleAsync();

        Assert.Equal("Security Operations", service.Name);
        Assert.Equal("Team Blue", service.Owner);
        Assert.Equal("Production", service.LifecycleStatus);
        Assert.Equal(["Sentinel", "Purview"], service.GetOrderedProductLinks().Select(x => x.ProductCatalogItem.Name).ToArray());
        Assert.Equal("Create", audit.Action);
    }

    [Fact]
    public async Task CreatePostAllowsRepeatedProductsInServiceFlow()
    {
        await using var fixture = await TestFixture.CreateAsync();
        await fixture.SeedConfigurableOptionsAsync();

        var products = await fixture.SeedProductsAsync("Portal", "Gateway", "Queue");
        using var controller = fixture.CreateController();

        var result = await controller.Create(new ServiceEditViewModel
        {
            Name = "Loopback Flow",
            Owner = "Team Blue",
            LifecycleStatus = "Production",
            ProductRows =
            [
                new ServiceProductRowViewModel { ProductId = products[0].Id },
                new ServiceProductRowViewModel { ProductId = products[1].Id },
                new ServiceProductRowViewModel { ProductId = products[0].Id },
                new ServiceProductRowViewModel { ProductId = products[2].Id }
            ]
        });

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(ServicesController.Index), redirect.ActionName);

        var service = await fixture.DbContext.ServiceCatalogItems
            .Include(x => x.ProductLinks)
            .ThenInclude(x => x.ProductCatalogItem)
            .SingleAsync();

        Assert.Equal(
            ["Portal", "Gateway", "Portal", "Queue"],
            service.GetOrderedProductLinks().Select(x => x.ProductCatalogItem.Name).ToArray());
    }

    [Fact]
    public async Task EditPostRebuildsOrderedProductLinksAndWritesAudit()
    {
        await using var fixture = await TestFixture.CreateAsync();
        await fixture.SeedConfigurableOptionsAsync();

        var products = await fixture.SeedProductsAsync("Atlas", "Broker", "Core");
        var service = new ServiceCatalogItem
        {
            Name = "Payments",
            Description = "Existing flow",
            Owner = "Team Blue",
            LifecycleStatus = "Trial",
            ProductLinks =
            [
                new ServiceCatalogItemProduct { ProductCatalogItemId = products[0].Id, SortOrder = 1 },
                new ServiceCatalogItemProduct { ProductCatalogItemId = products[1].Id, SortOrder = 2 }
            ]
        };

        fixture.DbContext.ServiceCatalogItems.Add(service);
        await fixture.DbContext.SaveChangesAsync();

        using var controller = fixture.CreateController();
        var result = await controller.Edit(service.Id, new ServiceEditViewModel
        {
            Name = "Payments Revised",
            Description = "Updated flow",
            Owner = " Team Green ",
            LifecycleStatus = " Production ",
            ProductRows =
            [
                new ServiceProductRowViewModel { ProductId = products[2].Id },
                new ServiceProductRowViewModel { ProductId = products[0].Id },
                new ServiceProductRowViewModel { ProductId = products[1].Id }
            ]
        });

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(ServicesController.Index), redirect.ActionName);

        var updatedService = await fixture.DbContext.ServiceCatalogItems
            .Include(x => x.ProductLinks)
            .ThenInclude(x => x.ProductCatalogItem)
            .SingleAsync(x => x.Id == service.Id);
        var audit = await fixture.DbContext.AuditLogEntries.SingleAsync();

        Assert.Equal("Payments Revised", updatedService.Name);
        Assert.Equal("Team Green", updatedService.Owner);
        Assert.Equal("Production", updatedService.LifecycleStatus);
        Assert.Equal(["Core", "Atlas", "Broker"], updatedService.GetOrderedProductLinks().Select(x => x.ProductCatalogItem.Name).ToArray());
        Assert.Equal("Update", audit.Action);
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
    public async Task VisualizeReturnsConnectionsInSavedOrder()
    {
        await using var fixture = await TestFixture.CreateAsync();

        var products = await fixture.SeedProductsAsync("Portal", "API", "Queue");
        var service = new ServiceCatalogItem
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
        };

        fixture.DbContext.ServiceCatalogItems.Add(service);
        await fixture.DbContext.SaveChangesAsync();

        var result = await fixture.CreateController().Visualize(service.Id);

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<ServiceVisualizationViewModel>(view.Model);

        Assert.Equal(["Portal", "API", "Queue"], model.ProductNames.ToArray());
        Assert.Equal(2, model.Connections.Count);
        Assert.Equal("Portal", model.Connections[0].FromProductName);
        Assert.Equal("API", model.Connections[0].ToProductName);
        Assert.Equal("API", model.Connections[1].FromProductName);
        Assert.Equal("Queue", model.Connections[1].ToProductName);
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
