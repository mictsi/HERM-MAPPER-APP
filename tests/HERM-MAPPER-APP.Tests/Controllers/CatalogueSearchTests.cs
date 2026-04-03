using HERMMapperApp.Controllers;
using HERMMapperApp.Data;
using HERMMapperApp.Models;
using HERMMapperApp.Services;
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

public sealed class CatalogueSearchTests
{
    [Fact]
    public async Task ProductsIndexSearchMatchesPartialStringsCaseInsensitivelyAsync()
    {
        await using var fixture = await TestFixture.CreateAsync();
        await fixture.DbContext.ConfigurableFieldOptions.AddRangeAsync(
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
        await fixture.DbContext.ProductCatalogItems.AddRangeAsync(
            CreateProduct("SharePoint Online", "Microsoft", "Production", "Collaboration Team"),
            CreateProduct("ServiceNow", "ServiceNow", null));
        await fixture.DbContext.SaveChangesAsync();

        using var controller = fixture.CreateProductsController();

        var result = await controller.IndexAsync("point");

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<ProductsIndexViewModel>(view.Model);
        var product = Assert.Single(model.Products);
        Assert.Equal("SharePoint Online", product.Name);
    }

    [Fact]
    public async Task ProductsIndexFiltersByMultipleOwnersAndLifecycleAsync()
    {
        await using var fixture = await TestFixture.CreateAsync();
        await fixture.DbContext.ConfigurableFieldOptions.AddRangeAsync(
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
        await fixture.DbContext.ProductCatalogItems.AddRangeAsync(
            CreateProduct("Payments Hub", null, "Production", "Finance Team"),
            CreateProduct("Platform Core", null, "Production", "Platform Team"),
            CreateProduct("Developer Portal", null, "Trial", "Platform Team"));
        await fixture.DbContext.SaveChangesAsync();

        using var controller = fixture.CreateProductsController();

        var result = await controller.IndexAsync(null, ["Finance Team", "Platform Team"], "Production");

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<ProductsIndexViewModel>(view.Model);
        Assert.Equal(["Finance Team", "Platform Team"], model.SelectedOwners.OrderBy(x => x).ToArray());
        Assert.Equal(["Payments Hub", "Platform Core"], model.Products.Select(x => x.Name).ToArray());
    }

    [Fact]
    public async Task ProductsIndexExcludesDeletedProductsAsync()
    {
        await using var fixture = await TestFixture.CreateAsync();
        await fixture.DbContext.ProductCatalogItems.AddRangeAsync(
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
        var result = await controller.IndexAsync(null);

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<ProductsIndexViewModel>(view.Model);
        Assert.Collection(model.Products, product => Assert.Equal("Visible Product", product.Name));
    }

    [Fact]
    public async Task ReferenceIndexSearchMatchesPartialStringsForTypeCapabilityAndDomainAsync()
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

        await fixture.DbContext.TrmDomains.AddRangeAsync(finance, security);
        await fixture.DbContext.TrmCapabilities.AddRangeAsync(payments, identity);
        await fixture.DbContext.TrmComponents.AddRangeAsync(customComponent, modelComponent);
        await fixture.DbContext.SaveChangesAsync();

        await fixture.DbContext.TrmComponentCapabilityLinks.AddRangeAsync(
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

    [Fact]
    public async Task ReferenceIndexSearchAndSelectionIncludeArmAndBrmModelsAsync()
    {
        await using var fixture = await TestFixture.CreateAsync();

        var armDomain = new ArmDomain
        {
            Code = "AD001",
            Name = "Applications"
        };
        var armCapability = new ArmCapability
        {
            Code = "AP001",
            Name = "Case Management",
            ParentDomain = armDomain,
            ParentDomainCode = armDomain.Code
        };
        var armComponent = new ArmComponent
        {
            Code = "AC001",
            Name = "Workflow Engine",
            ParentCapability = armCapability,
            ParentCapabilityCode = armCapability.Code,
            ProductExamples = "Flow Suite"
        };
        armComponent.CapabilityLinks.Add(new ArmComponentCapabilityLink
        {
            ArmComponent = armComponent,
            ArmCapability = armCapability
        });

        var brmDomain = new BrmDomain
        {
            Code = "BD001",
            Name = "Operations"
        };
        var brmCapability = new BrmCapability
        {
            Code = "BC001",
            Name = "Order Handling",
            ParentDomain = brmDomain,
            ParentDomainCode = brmDomain.Code
        };
        var brmComponent = new BrmComponent
        {
            Code = "BC002",
            Name = "Order Capture",
            ParentCapability = brmCapability,
            ParentCapabilityCode = brmCapability.Code,
            ProductExamples = "Capture Cloud"
        };

        await fixture.DbContext.AddRangeAsync(armDomain, armCapability, armComponent, brmDomain, brmCapability, brmComponent);
        await fixture.DbContext.SaveChangesAsync();

        using var controller = fixture.CreateReferenceController();

        var searchResult = await controller.IndexAsync("arm workflow", null, null);
        var searchModel = Assert.IsType<ReferenceCatalogueViewModel>(Assert.IsType<ViewResult>(searchResult).Model);
        var searchComponent = Assert.Single(searchModel.Components);
        Assert.Equal("Workflow Engine", searchComponent.Name);
        Assert.Equal(ReferenceModelKind.Arm, searchComponent.ModelKind);
        var searchGroup = Assert.Single(searchModel.ModelGroups);
        Assert.Equal(ReferenceModelKind.Arm, searchGroup.ModelKind);
        Assert.True(searchGroup.IsExpanded);

        var selectionResult = await controller.IndexAsync(null, null, null, ReferenceModelKind.Brm, "BD001", "BC001");
        var selectionModel = Assert.IsType<ReferenceCatalogueViewModel>(Assert.IsType<ViewResult>(selectionResult).Model);
        var selectedComponent = Assert.Single(selectionModel.Components);
        Assert.Equal("Order Capture", selectedComponent.Name);
        Assert.Equal(ReferenceModelKind.Brm, selectionModel.SelectedModelKind);
        Assert.Equal("BD001", selectionModel.SelectedDomainCode);
        Assert.Equal("BC001", selectionModel.SelectedCapabilityCode);
        Assert.Equal("browser-model-brm-domain-bd001-capability-bc001", selectionModel.ActiveTreeAnchorId);
        Assert.Contains(selectionModel.ModelGroups, group => group.ModelKind == ReferenceModelKind.Brm && group.IsExpanded);
        Assert.Contains(selectionModel.ModelGroups, group => group.ModelKind == ReferenceModelKind.Arm && !group.IsExpanded);
    }

    [Fact]
    public async Task ReferenceIndexDefaultsToCollapsedAllModelsTreeAsync()
    {
        await using var fixture = await TestFixture.CreateAsync();

        await fixture.DbContext.AddRangeAsync(
            new ArmDomain
            {
                Code = "AD001",
                Name = "Applications"
            },
            new BrmDomain
            {
                Code = "BD001",
                Name = "Business"
            });
        await fixture.DbContext.SaveChangesAsync();

        using var controller = fixture.CreateReferenceController();

        var result = await controller.IndexAsync(null, null, null);

        var model = Assert.IsType<ReferenceCatalogueViewModel>(Assert.IsType<ViewResult>(result).Model);
        Assert.Equal("browser-navigation", model.ActiveTreeAnchorId);
        Assert.All(model.ModelGroups, group => Assert.False(group.IsExpanded));
    }

    private sealed class TestFixture : IAsyncDisposable
    {
        private readonly SqliteConnection connection;
        private readonly StubHttpMessageHandler aiHttpMessageHandler = new();
        private readonly HttpClient aiHttpClient;

        private TestFixture(SqliteConnection connection, AppDbContext dbContext)
        {
            this.connection = connection;
            aiHttpClient = new HttpClient(aiHttpMessageHandler);
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
                new ConfigurableFieldService(DbContext),
                CreateAiProductMappingService());

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
            aiHttpClient.Dispose();
            aiHttpMessageHandler.Dispose();
            await DbContext.DisposeAsync();
            await connection.DisposeAsync();
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
    }

    private sealed class TestTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();

        public void SaveTempData(HttpContext context, IDictionary<string, object> values)
        {
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
