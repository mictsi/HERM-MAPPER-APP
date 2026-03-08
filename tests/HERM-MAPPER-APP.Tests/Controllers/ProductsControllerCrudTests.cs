using HERM_MAPPER_APP.Controllers;
using HERM_MAPPER_APP.Data;
using HERM_MAPPER_APP.Models;
using HERM_MAPPER_APP.Services;
using HERM_MAPPER_APP.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HERM_MAPPER_APP.Tests.Controllers;

public sealed class ProductsControllerCrudTests
{
    [Fact]
    public async Task Create_Post_PersistsProduct_NormalizesSelections_AndWritesAudit()
    {
        await using var fixture = await TestFixture.CreateAsync();
        await fixture.SeedConfigurableOptionsAsync();
        var controller = fixture.CreateController();

        var result = await controller.Create(new ProductEditViewModel
        {
            Name = " Sentinel ",
            Vendor = "Microsoft",
            LifecycleStatus = " Production ",
            Owners = [" Team Blue ", "Team Blue", "Team Red", ""],
            Description = "Security analytics"
        });

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(ProductsController.Index), redirect.ActionName);

        var product = await fixture.DbContext.ProductCatalogItems
            .Include(x => x.Owners)
            .SingleAsync();
        var audit = await fixture.DbContext.AuditLogEntries.SingleAsync();

        Assert.Equal(" Sentinel ", product.Name);
        Assert.Equal("Production", product.LifecycleStatus);
        Assert.Equal(["Team Blue", "Team Red"], product.OwnerValues);
        Assert.Equal("Create", audit.Action);
    }

    [Fact]
    public async Task Edit_Post_UpdatesProductAndSynchronizesOwners()
    {
        await using var fixture = await TestFixture.CreateAsync();
        await fixture.SeedConfigurableOptionsAsync();

        var product = new ProductCatalogItem
        {
            Name = "Sentinel",
            Vendor = "Microsoft",
            LifecycleStatus = "Trial",
            Owners =
            [
                new ProductCatalogItemOwner { OwnerValue = "Team Blue" },
                new ProductCatalogItemOwner { OwnerValue = "Team Red" }
            ]
        };
        fixture.DbContext.ProductCatalogItems.Add(product);
        await fixture.DbContext.SaveChangesAsync();

        var controller = fixture.CreateController();
        var result = await controller.Edit(product.Id, new ProductEditViewModel
        {
            Name = "Sentinel X",
            Vendor = "Contoso",
            LifecycleStatus = " Production ",
            Owners = ["Team Green", "Team Blue", "team green"],
            Description = "Updated"
        });

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(ProductsController.Index), redirect.ActionName);

        var updatedProduct = await fixture.DbContext.ProductCatalogItems
            .Include(x => x.Owners)
            .SingleAsync(x => x.Id == product.Id);
        var audit = await fixture.DbContext.AuditLogEntries.SingleAsync();

        Assert.Equal("Sentinel X", updatedProduct.Name);
        Assert.Equal("Contoso", updatedProduct.Vendor);
        Assert.Equal("Production", updatedProduct.LifecycleStatus);
        Assert.Equal(["Team Blue", "Team Green"], updatedProduct.OwnerValues);
        Assert.Equal("Update", audit.Action);
    }

    [Fact]
    public async Task Visualize_ReturnsDistinctPaths_UsingFallbackHierarchy()
    {
        await using var fixture = await TestFixture.CreateAsync();

        var domain = new TrmDomain { Code = "TD001", Name = "Technology" };
        var capability = new TrmCapability { Code = "TP001", Name = "Observability", ParentDomain = domain, ParentDomainCode = domain.Code };
        var component = new TrmComponent { Code = "TC001", Name = "Monitoring", ParentCapability = capability, ParentCapabilityCode = capability.Code };
        var product = new ProductCatalogItem { Name = "Sentinel" };

        fixture.DbContext.AddRange(domain, capability, component, product);
        await fixture.DbContext.SaveChangesAsync();

        fixture.DbContext.ProductMappings.AddRange(
            new ProductMapping
            {
                ProductCatalogItemId = product.Id,
                TrmDomainId = domain.Id,
                TrmCapabilityId = capability.Id,
                TrmComponentId = component.Id,
                MappingStatus = MappingStatus.Complete
            },
            new ProductMapping
            {
                ProductCatalogItemId = product.Id,
                TrmDomainId = domain.Id,
                TrmCapabilityId = capability.Id,
                TrmComponentId = component.Id,
                MappingStatus = MappingStatus.Complete
            },
            new ProductMapping
            {
                ProductCatalogItemId = product.Id,
                TrmDomainId = domain.Id,
                TrmCapabilityId = capability.Id,
                MappingStatus = MappingStatus.Draft
            });
        await fixture.DbContext.SaveChangesAsync();

        var result = await fixture.CreateController().Visualize(product.Id);

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<ProductVisualizationViewModel>(view.Model);

        Assert.Equal(2, model.Paths.Count);
        Assert.Contains(model.Paths, path => path.ComponentLabel == "-" && path.Status == "Draft");
        Assert.Contains(model.Paths, path => path.ComponentLabel == "TC001 Monitoring" && path.Status == "Complete");
    }

    [Fact]
    public async Task DeleteConfirmed_RemovesProduct_AndWritesAudit()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var product = new ProductCatalogItem { Name = "Sentinel" };
        fixture.DbContext.ProductCatalogItems.Add(product);
        await fixture.DbContext.SaveChangesAsync();

        var result = await fixture.CreateController().DeleteConfirmed(product.Id);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(ProductsController.Index), redirect.ActionName);
        Assert.Empty(await fixture.DbContext.ProductCatalogItems.ToListAsync());
        Assert.Equal("Delete", (await fixture.DbContext.AuditLogEntries.SingleAsync()).Action);
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

        public ProductsController CreateController() =>
            new(DbContext, new AuditLogService(DbContext), new ConfigurableFieldService(DbContext));

        public async Task SeedConfigurableOptionsAsync()
        {
            DbContext.ConfigurableFieldOptions.AddRange(
                new ConfigurableFieldOption { FieldName = ConfigurableFieldNames.Owner, Value = "Team Blue", SortOrder = 1 },
                new ConfigurableFieldOption { FieldName = ConfigurableFieldNames.Owner, Value = "Team Red", SortOrder = 2 },
                new ConfigurableFieldOption { FieldName = ConfigurableFieldNames.LifecycleStatus, Value = "Production", SortOrder = 1 });
            await DbContext.SaveChangesAsync();
        }

        public async ValueTask DisposeAsync()
        {
            await DbContext.DisposeAsync();
            await connection.DisposeAsync();
        }
    }
}
