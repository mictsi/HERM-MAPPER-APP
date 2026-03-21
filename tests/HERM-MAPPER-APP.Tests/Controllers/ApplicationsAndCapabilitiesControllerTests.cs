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

public sealed class ApplicationsAndCapabilitiesControllerTests
{
    [Fact]
    public async Task ApplicationCreatePersistsMappingsAndDetailsResolveTrmPaths()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var seeded = await fixture.SeedHermAlignmentAsync();

        using var controller = fixture.CreateApplicationsController();
        var createResult = await controller.Create(new ApplicationEditViewModel
        {
            Name = "Admissions Hub",
            Description = "Applicant workflow",
            MappingRows =
            [
                new ApplicationMappingRowInputViewModel
                {
                    ArmComponentId = seeded.ArmComponent.Id,
                    ProductCatalogItemId = seeded.Product.Id,
                    IsPrimary = true
                }
            ]
        });

        var redirect = Assert.IsType<RedirectToActionResult>(createResult);
        Assert.Equal(nameof(ApplicationsController.Details), redirect.ActionName);

        var application = await fixture.DbContext.ApplicationCatalogItems
            .Include(x => x.Mappings)
            .SingleAsync();
        Assert.Single(application.Mappings);
        Assert.Equal(seeded.ArmComponent.Id, application.Mappings.Single().ArmComponentId);
        Assert.Equal(seeded.Product.Id, application.Mappings.Single().ProductCatalogItemId);

        using var detailsController = fixture.CreateApplicationsController();
        var detailsResult = await detailsController.Details(application.Id);

        var view = Assert.IsType<ViewResult>(detailsResult);
        var model = Assert.IsType<ApplicationDetailsViewModel>(view.Model);
        var path = Assert.Single(model.ResolvedPaths);
        Assert.Equal("AC001 Applicant Portal", path.ArmComponentLabel);
        Assert.Equal("TC001 Integration Platform", path.TrmComponentLabel);
        Assert.Equal("Complete", path.MappingStatus);
    }

    [Fact]
    public async Task CapabilityDetailsResolveApplicationsProductsAndTrmPaths()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var seeded = await fixture.SeedHermAlignmentAsync();

        var capability = new BusinessCapabilityCatalogItem
        {
            Name = "Student Recruitment",
            Mappings =
            [
                new BusinessCapabilityCatalogItemMapping
                {
                    BrmComponentId = seeded.BrmComponent.Id,
                    ArmComponentId = seeded.ArmComponent.Id,
                    IsPrimary = true
                }
            ]
        };

        var application = new ApplicationCatalogItem
        {
            Name = "Admissions Hub",
            Mappings =
            [
                new ApplicationCatalogItemMapping
                {
                    ArmComponentId = seeded.ArmComponent.Id,
                    ProductCatalogItemId = seeded.Product.Id,
                    IsPrimary = true
                }
            ]
        };

        await fixture.DbContext.AddRangeAsync(capability, application);
        await fixture.DbContext.SaveChangesAsync();

        using var controller = fixture.CreateCapabilitiesController();
        var result = await controller.Details(capability.Id);

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<CapabilityDetailsViewModel>(view.Model);
        var path = Assert.Single(model.ResolvedPaths);
        Assert.Equal("BC002 Student Recruitment", path.BrmComponentLabel);
        Assert.Equal("AC001 Applicant Portal", path.ArmComponentLabel);
        Assert.Equal("Admissions Hub", path.ApplicationName);
        Assert.Equal("Contoso Platform (Contoso)", path.ProductLabel);
        Assert.Equal("TC001 Integration Platform", path.TrmComponentLabel);
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

        public ApplicationsController CreateApplicationsController()
        {
            var controller = new ApplicationsController(
                DbContext,
                new AuditLogService(DbContext),
                new HermDrilldownService(DbContext));

            controller.TempData = new TempDataDictionary(new DefaultHttpContext(), new TestTempDataProvider());
            return controller;
        }

        public CapabilitiesController CreateCapabilitiesController()
        {
            var controller = new CapabilitiesController(
                DbContext,
                new AuditLogService(DbContext),
                new HermDrilldownService(DbContext));

            controller.TempData = new TempDataDictionary(new DefaultHttpContext(), new TestTempDataProvider());
            return controller;
        }

        public async Task<SeededHermAlignment> SeedHermAlignmentAsync()
        {
            var armDomain = new ArmDomain { Code = "AD001", Name = "Student" };
            var armCapability = new ArmCapability
            {
                Code = "AP001",
                Name = "Recruitment",
                ParentDomain = armDomain,
                ParentDomainCode = armDomain.Code
            };
            var armComponent = new ArmComponent
            {
                Code = "AC001",
                Name = "Applicant Portal",
                ParentCapability = armCapability,
                ParentCapabilityCode = armCapability.Code
            };

            var brmDomain = new BrmDomain { Code = "BD001", Name = "Student Lifecycle" };
            var brmCapability = new BrmCapability
            {
                Code = "BC001",
                Name = "Student Management",
                ParentDomain = brmDomain,
                ParentDomainCode = brmDomain.Code
            };
            var brmComponent = new BrmComponent
            {
                Code = "BC002",
                Name = "Student Recruitment",
                ParentCapability = brmCapability,
                ParentCapabilityCode = brmCapability.Code
            };

            var trmDomain = new TrmDomain { Code = "TD001", Name = "Integration" };
            var trmCapability = new TrmCapability
            {
                Code = "TP001",
                Name = "API and Messaging",
                ParentDomain = trmDomain,
                ParentDomainCode = trmDomain.Code
            };
            var trmComponent = new TrmComponent
            {
                Code = "TC001",
                Name = "Integration Platform",
                ParentCapability = trmCapability,
                ParentCapabilityCode = trmCapability.Code
            };

            var product = new ProductCatalogItem
            {
                Name = "Contoso Platform",
                Vendor = "Contoso",
                Mappings =
                [
                    new ProductMapping
                    {
                        TrmDomain = trmDomain,
                        TrmCapability = trmCapability,
                        TrmComponent = trmComponent,
                        MappingStatus = MappingStatus.Complete
                    }
                ]
            };

            await DbContext.AddRangeAsync(
                armDomain,
                armCapability,
                armComponent,
                brmDomain,
                brmCapability,
                brmComponent,
                trmDomain,
                trmCapability,
                trmComponent,
                product);
            await DbContext.SaveChangesAsync();

            return new SeededHermAlignment(armComponent, brmComponent, product);
        }

        public async ValueTask DisposeAsync()
        {
            await DbContext.DisposeAsync();
            await connection.DisposeAsync();
        }
    }

    private sealed record SeededHermAlignment(
        ArmComponent ArmComponent,
        BrmComponent BrmComponent,
        ProductCatalogItem Product);

    private sealed class TestTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();

        public void SaveTempData(HttpContext context, IDictionary<string, object> values)
        {
        }
    }
}
