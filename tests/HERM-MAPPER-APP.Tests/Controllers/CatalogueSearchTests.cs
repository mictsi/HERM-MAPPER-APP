using HERMMapperApp.Controllers;
using HERMMapperApp.Data;
using HERMMapperApp.Models;
using HERMMapperApp.Services;
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

public sealed class CatalogueSearchTests
{
    [Fact]
    public async Task ProductsIndexSearchMatchesPartialStringsCaseInsensitively()
    {
        await using var fixture = await TestFixture.CreateAsync();
        fixture.DbContext.ConfigurableFieldOptions.AddRange(
            new ConfigurableFieldOption
            {
                FieldName = ConfigurableFieldNames.Owner,
                Value = "Collaboration Team",
                SortOrder = 1
            },
            new ConfigurableFieldOption
            {
                FieldName = ConfigurableFieldNames.LifecycleStatus,
                Value = "Production",
                SortOrder = 1
            });
        fixture.DbContext.ProductCatalogItems.AddRange(
            CreateProduct("SharePoint Online", "Microsoft", "Production", "Collaboration Team"),
            CreateProduct("ServiceNow", "ServiceNow", null));
        await fixture.DbContext.SaveChangesAsync();

        using var controller = fixture.CreateProductsController();

        var result = await controller.Index("point");

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<ProductsIndexViewModel>(view.Model);
        var product = Assert.Single(model.Products);
        Assert.Equal("SharePoint Online", product.Name);
    }

    [Fact]
    public async Task ProductsIndexFiltersByMultipleOwnersAndLifecycle()
    {
        await using var fixture = await TestFixture.CreateAsync();
        fixture.DbContext.ConfigurableFieldOptions.AddRange(
            new ConfigurableFieldOption
            {
                FieldName = ConfigurableFieldNames.Owner,
                Value = "Platform Team",
                SortOrder = 1
            },
            new ConfigurableFieldOption
            {
                FieldName = ConfigurableFieldNames.Owner,
                Value = "Finance Team",
                SortOrder = 2
            },
            new ConfigurableFieldOption
            {
                FieldName = ConfigurableFieldNames.LifecycleStatus,
                Value = "Production",
                SortOrder = 1
            },
            new ConfigurableFieldOption
            {
                FieldName = ConfigurableFieldNames.LifecycleStatus,
                Value = "Trial",
                SortOrder = 2
            });
        fixture.DbContext.ProductCatalogItems.AddRange(
            CreateProduct("Payments Hub", null, "Production", "Finance Team"),
            CreateProduct("Platform Core", null, "Production", "Platform Team"),
            CreateProduct("Developer Portal", null, "Trial", "Platform Team"));
        await fixture.DbContext.SaveChangesAsync();

        using var controller = fixture.CreateProductsController();

        var result = await controller.Index(null, ["Finance Team", "Platform Team"], "Production");

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<ProductsIndexViewModel>(view.Model);
        Assert.Equal(["Finance Team", "Platform Team"], model.SelectedOwners.OrderBy(x => x).ToArray());
        Assert.Equal(["Payments Hub", "Platform Core"], model.Products.Select(x => x.Name).ToArray());
    }

    [Fact]
    public async Task ProductsIndexExcludesDeletedProducts()
    {
        await using var fixture = await TestFixture.CreateAsync();
        fixture.DbContext.ProductCatalogItems.AddRange(
            CreateProduct("Visible Product", null, null),
            new ProductCatalogItem
            {
                Name = "Deleted Product",
                IsDeleted = true,
                DeletedUtc = DateTime.UtcNow,
                DeletedReason = "Moved to trash from the product catalogue."
            });
        await fixture.DbContext.SaveChangesAsync();

        using var controller = fixture.CreateProductsController();
        var result = await controller.Index(null);

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<ProductsIndexViewModel>(view.Model);
        Assert.Collection(model.Products, product => Assert.Equal("Visible Product", product.Name));
    }

    [Fact]
    public async Task ReferenceIndexSearchMatchesPartialStringsForTypeCapabilityAndDomain()
    {
        await using var fixture = await TestFixture.CreateAsync();

        var finance = new TrmDomain
        {
            Code = "DOM-FIN",
            Name = "Finance"
        };
        var payments = new TrmCapability
        {
            Code = "CAP-PAY",
            Name = "Payments",
            ParentDomain = finance
        };
        var customComponent = new TrmComponent
        {
            Code = "CMP-CUST",
            TechnologyComponentCode = "TC-900",
            Name = "Ledger Hub",
            IsCustom = true,
            ProductExamples = "LedgerPro"
        };

        var security = new TrmDomain
        {
            Code = "DOM-SEC",
            Name = "Security"
        };
        var identity = new TrmCapability
        {
            Code = "CAP-ID",
            Name = "Identity",
            ParentDomain = security
        };
        var modelComponent = new TrmComponent
        {
            Code = "CMP-MOD",
            Name = "Access Gateway",
            IsCustom = false
        };

        fixture.DbContext.TrmDomains.AddRange(finance, security);
        fixture.DbContext.TrmCapabilities.AddRange(payments, identity);
        fixture.DbContext.TrmComponents.AddRange(customComponent, modelComponent);
        await fixture.DbContext.SaveChangesAsync();

        fixture.DbContext.TrmComponentCapabilityLinks.AddRange(
            new TrmComponentCapabilityLink
            {
                TrmComponentId = customComponent.Id,
                TrmCapabilityId = payments.Id
            },
            new TrmComponentCapabilityLink
            {
                TrmComponentId = modelComponent.Id,
                TrmCapabilityId = identity.Id
            });
        await fixture.DbContext.SaveChangesAsync();

        using var controller = fixture.CreateReferenceController();

        var typeResult = await controller.IndexAsync("cust", null, null);
        var typeModel = Assert.IsType<ReferenceCatalogueViewModel>(Assert.IsType<ViewResult>(typeResult).Model);
        Assert.Collection(typeModel.Components, component => Assert.Equal("Ledger Hub", component.Name));

        var capabilityResult = await controller.IndexAsync("pay", null, null);
        var capabilityModel = Assert.IsType<ReferenceCatalogueViewModel>(Assert.IsType<ViewResult>(capabilityResult).Model);
        Assert.Collection(capabilityModel.Components, component => Assert.Equal("Ledger Hub", component.Name));

        var domainResult = await controller.IndexAsync("fin", null, null);
        var domainModel = Assert.IsType<ReferenceCatalogueViewModel>(Assert.IsType<ViewResult>(domainResult).Model);
        Assert.Collection(domainModel.Components, component => Assert.Equal("Ledger Hub", component.Name));
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

        public ProductsController CreateProductsController() =>
            new(
                DbContext,
                new AuditLogService(DbContext),
                new ConfigurableFieldService(DbContext));

        public ReferenceController CreateReferenceController()
        {
            var controller = new ReferenceController(
                DbContext,
                new TrmWorkbookImportService(DbContext, new ComponentVersioningService(DbContext), new AuditLogService(DbContext)),
                new ComponentVersioningService(DbContext),
                new AuditLogService(DbContext),
                new TestWebHostEnvironment());

            controller.TempData = new TempDataDictionary(new DefaultHttpContext(), new TestTempDataProvider());
            return controller;
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

    private sealed class TestWebHostEnvironment : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "HERM-MAPPER-APP.Tests";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = string.Empty;
        public string EnvironmentName { get; set; } = "Development";
        public string ContentRootPath { get; set; } = System.IO.Path.GetTempPath();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private static ProductCatalogItem CreateProduct(string name, string? vendor, string? lifecycleStatus, params string[] owners)
    {
        var product = new ProductCatalogItem
        {
            Name = name,
            Vendor = vendor,
            LifecycleStatus = lifecycleStatus
        };

        foreach (var owner in owners)
        {
            product.Owners.Add(new ProductCatalogItemOwner
            {
                OwnerValue = owner
            });
        }

        return product;
    }
}
