using HERMMapperApp.Controllers;
using HERMMapperApp.Data;
using HERMMapperApp.Models;
using HERMMapperApp.Services;
using HERMMapperApp.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HERMMapperApp.Tests.Controllers;

public sealed class MappingsControllerTests
{
    [Fact]
    public async Task CapabilitiesReturnsFilteredCapabilities()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var domainA = new TrmDomain { Code = "TD001", Name = "Technology" };
        var domainB = new TrmDomain { Code = "TD002", Name = "Security" };
        var capabilityA = new TrmCapability { Code = "TP001", Name = "Observability", ParentDomain = domainA, ParentDomainCode = domainA.Code };
        var capabilityB = new TrmCapability { Code = "TP002", Name = "Identity", ParentDomain = domainB, ParentDomainCode = domainB.Code };
        fixture.DbContext.AddRange(domainA, domainB, capabilityA, capabilityB);
        await fixture.DbContext.SaveChangesAsync();

        var result = await fixture.CreateController().Capabilities(domainA.Id);

        var json = Assert.IsType<JsonResult>(result);
        var items = ToDictionaryList(json.Value);
        Assert.Single(items);
        Assert.Equal(capabilityA.Id, items[0]["id"]);
        Assert.Equal("TP001 Observability", items[0]["text"]);
    }

    [Fact]
    public async Task ComponentsExcludesDeletedComponentsAndOrdersModelBeforeCustom()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var domain = new TrmDomain { Code = "TD001", Name = "Technology" };
        var capability = new TrmCapability { Code = "TP001", Name = "Observability", ParentDomain = domain, ParentDomainCode = domain.Code };
        var modelComponent = new TrmComponent { Code = "TC001", Name = "Monitoring", ParentCapability = capability, ParentCapabilityCode = capability.Code };
        var customComponent = new TrmComponent
        {
            Code = "CUS00000001",
            TechnologyComponentCode = "TECH-1",
            Name = "Custom Monitoring",
            ParentCapability = capability,
            ParentCapabilityCode = capability.Code,
            IsCustom = true
        };
        var deletedComponent = new TrmComponent
        {
            Code = "TC999",
            Name = "Deleted",
            ParentCapability = capability,
            ParentCapabilityCode = capability.Code,
            IsDeleted = true
        };
        fixture.DbContext.AddRange(domain, capability, modelComponent, customComponent, deletedComponent);
        await fixture.DbContext.SaveChangesAsync();

        var result = await fixture.CreateController().Components(capability.Id);

        var json = Assert.IsType<JsonResult>(result);
        var items = ToDictionaryList(json.Value);
        Assert.Equal(2, items.Count);
        Assert.Equal("TC001 Monitoring", items[0]["text"]);
        Assert.Equal("TECH-1 Custom Monitoring", items[1]["text"]);
    }

    [Fact]
    public async Task CreatePostWithCustomComponentCreatesMappingComponentHistoryAndAudit()
    {
        await using var fixture = await TestFixture.CreateAsync();
        await fixture.SeedOwnerOptionsAsync();

        var domain = new TrmDomain { Code = "TD001", Name = "Technology" };
        var capability = new TrmCapability { Code = "TP001", Name = "Observability", ParentDomain = domain, ParentDomainCode = domain.Code };
        var product = new ProductCatalogItem { Name = "Sentinel" };
        fixture.DbContext.AddRange(domain, capability, product);
        await fixture.DbContext.SaveChangesAsync();

        var result = await fixture.CreateController().Create(new MappingEditViewModel
        {
            ProductId = product.Id,
            Owners = ["Team Blue"],
            SelectedDomainId = domain.Id,
            SelectedCapabilityId = capability.Id,
            CustomTechnologyComponentCode = "TECH-42",
            CustomComponentName = "Custom Hub",
            MappingStatus = MappingStatus.Complete,
            MappingRationale = "Needed"
        });

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(MappingsController.Index), redirect.ActionName);

        var mapping = await fixture.DbContext.ProductMappings.SingleAsync();
        var component = await fixture.DbContext.TrmComponents
            .Include(x => x.CapabilityLinks)
            .SingleAsync();
        var versions = await fixture.DbContext.TrmComponentVersions.ToListAsync();
        var audits = await fixture.DbContext.AuditLogEntries.OrderBy(x => x.Category).ThenBy(x => x.Action).ToListAsync();
        var persistedProduct = await fixture.DbContext.ProductCatalogItems.Include(x => x.Owners).SingleAsync();

        Assert.Equal(domain.Id, mapping.TrmDomainId);
        Assert.Equal(capability.Id, mapping.TrmCapabilityId);
        Assert.Equal(component.Id, mapping.TrmComponentId);
        Assert.True(component.IsCustom);
        Assert.Equal("TECH-42", component.TechnologyComponentCode);
        Assert.Single(component.CapabilityLinks);
        Assert.Single(versions);
        Assert.Equal("Created", versions[0].ChangeType);
        Assert.Equal(["Team Blue"], persistedProduct.GetOwnerValues());
        Assert.Equal(2, audits.Count);
        Assert.Contains(audits, entry => entry.Category == "Mapping" && entry.Action == "Create");
        Assert.Contains(audits, entry => entry.Category == "Component" && entry.Action == "Create");
    }

    [Fact]
    public async Task ExportCsvReturnsOnlyCompletedMappingsWhenIncludeUnfinishedIsFalse()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var domain = new TrmDomain { Code = "TD001", Name = "Technology" };
        var capability = new TrmCapability { Code = "TP001", Name = "Observability", ParentDomain = domain, ParentDomainCode = domain.Code };
        var component = new TrmComponent { Code = "TC001", Name = "Monitoring", ParentCapability = capability, ParentCapabilityCode = capability.Code };
        var productA = new ProductCatalogItem { Name = "Sentinel" };
        var productB = new ProductCatalogItem { Name = "Draft Tool" };
        fixture.DbContext.AddRange(domain, capability, component, productA, productB);
        await fixture.DbContext.SaveChangesAsync();

        fixture.DbContext.ProductMappings.AddRange(
            new ProductMapping
            {
                ProductCatalogItemId = productA.Id,
                TrmDomainId = domain.Id,
                TrmCapabilityId = capability.Id,
                TrmComponentId = component.Id,
                MappingStatus = MappingStatus.Complete
            },
            new ProductMapping
            {
                ProductCatalogItemId = productB.Id,
                TrmDomainId = domain.Id,
                TrmCapabilityId = capability.Id,
                MappingStatus = MappingStatus.Draft
            });
        await fixture.DbContext.SaveChangesAsync();

        var result = await fixture.CreateController().ExportCsv(null, null, null, null, includeUnfinished: false);

        var file = Assert.IsType<FileContentResult>(result);
        Assert.Equal("text/csv", file.ContentType);
        var content = System.Text.Encoding.UTF8.GetString(file.FileContents);
        Assert.Contains("Sentinel", content);
        Assert.DoesNotContain("Draft Tool", content);
    }

    private static List<Dictionary<string, object?>> ToDictionaryList(object? value)
    {
        Assert.NotNull(value);
        return ((System.Collections.IEnumerable)value!)
            .Cast<object>()
            .Select(item => item.GetType()
                .GetProperties()
                .ToDictionary(property => property.Name, property => property.GetValue(item)))
            .ToList();
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

        public MappingsController CreateController() =>
            new(
                DbContext,
                new AuditLogService(DbContext),
                new ComponentVersioningService(DbContext),
                new ConfigurableFieldService(DbContext));

        public async Task SeedOwnerOptionsAsync()
        {
            DbContext.ConfigurableFieldOptions.Add(new ConfigurableFieldOption
            {
                FieldName = ConfigurableFieldNames.Owner,
                Value = "Team Blue",
                SortOrder = 1
            });
            await DbContext.SaveChangesAsync();
        }

        public async ValueTask DisposeAsync()
        {
            await DbContext.DisposeAsync();
            await connection.DisposeAsync();
        }
    }
}
