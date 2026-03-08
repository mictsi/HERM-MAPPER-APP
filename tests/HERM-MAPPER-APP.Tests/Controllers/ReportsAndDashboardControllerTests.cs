using HERM_MAPPER_APP.Controllers;
using HERM_MAPPER_APP.Data;
using HERM_MAPPER_APP.Models;
using HERM_MAPPER_APP.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HERM_MAPPER_APP.Tests.Controllers;

public sealed class ReportsAndDashboardControllerTests
{
    [Fact]
    public async Task ReportsIndex_BuildsHierarchySankeyAndLifecycleData()
    {
        await using var fixture = await TestFixture.CreateAsync();

        var domain = new TrmDomain
        {
            Code = "TD001",
            Name = "Technology"
        };
        var capability = new TrmCapability
        {
            Code = "TP001",
            Name = "Observability",
            ParentDomain = domain,
            ParentDomainCode = domain.Code
        };
        var component = new TrmComponent
        {
            Code = "TC001",
            Name = "Monitoring",
            ParentCapability = capability,
            ParentCapabilityCode = capability.Code
        };

        var mappedProduct = new ProductCatalogItem
        {
            Name = "Sentinel",
            LifecycleStatus = "Production",
            Owners =
            [
                new ProductCatalogItemOwner { OwnerValue = "Team Blue" },
                new ProductCatalogItemOwner { OwnerValue = "Team Red" }
            ]
        };
        var unassignedProduct = new ProductCatalogItem
        {
            Name = "Legacy Tool"
        };
        var trialProduct = new ProductCatalogItem
        {
            Name = "Pilot Tool",
            LifecycleStatus = "Trial"
        };

        fixture.DbContext.AddRange(domain, capability, component, mappedProduct, unassignedProduct, trialProduct);
        await fixture.DbContext.SaveChangesAsync();

        fixture.DbContext.ProductMappings.Add(new ProductMapping
        {
            ProductCatalogItemId = mappedProduct.Id,
            TrmDomainId = domain.Id,
            TrmCapabilityId = capability.Id,
            TrmComponentId = component.Id,
            MappingStatus = MappingStatus.Complete
        });
        await fixture.DbContext.SaveChangesAsync();

        var result = await fixture.CreateReportsController().Index("Unassigned owner");

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<ReportsViewModel>(view.Model);

        Assert.Equal(2, model.OwnerCount);
        Assert.Equal(1, model.DomainCount);
        Assert.Equal(1, model.CapabilityCount);
        Assert.Equal(1, model.ComponentCount);
        Assert.Equal(1, model.ProductCount);
        Assert.Equal(2, model.MappingPathCount);
        Assert.Equal("Unassigned owner", model.SelectedLifecycleOwner);
        Assert.Equal(2, model.LifecycleProductCount);
        Assert.Equal(["Unassigned owner", "Team Blue", "Team Red"], model.AvailableOwners);

        Assert.Equal(["Not set", "Trial"], model.LifecycleStatuses.Select(x => x.Label).ToArray());
        Assert.Equal([1, 1], model.LifecycleStatuses.Select(x => x.ProductCount).ToArray());
        Assert.All(model.LifecycleStatuses, row => Assert.Equal(50.0m, row.Percentage));

        Assert.Equal(2, model.Owners.Count);
        Assert.All(model.Owners, owner =>
        {
            Assert.Equal("owner", owner.NodeType);
            Assert.Equal(1, owner.MappingCount);
            Assert.Single(owner.Children);
        });

        Assert.Equal(6, model.SankeyNodes.Count);
        Assert.Equal(5, model.SankeyLinks.Count);
        Assert.Contains(model.Paths, path => path.OwnerName == "Team Blue" && path.ProductName == "Sentinel");
        Assert.Contains(model.Paths, path => path.OwnerName == "Team Red" && path.ComponentLabel == "TC001 Monitoring");
    }

    [Fact]
    public async Task HomeIndex_ReturnsDashboardCountsAndRecentProducts()
    {
        await using var fixture = await TestFixture.CreateAsync();

        var domain = new TrmDomain
        {
            Code = "TD001",
            Name = "Technology"
        };
        var capability = new TrmCapability
        {
            Code = "TP001",
            Name = "Observability",
            ParentDomain = domain,
            ParentDomainCode = domain.Code
        };
        var activeComponent = new TrmComponent
        {
            Code = "TC001",
            Name = "Monitoring",
            ParentCapability = capability,
            ParentCapabilityCode = capability.Code
        };
        var deletedComponent = new TrmComponent
        {
            Code = "TC002",
            Name = "Retired",
            ParentCapability = capability,
            ParentCapabilityCode = capability.Code,
            IsDeleted = true
        };

        var products = Enumerable.Range(1, 7)
            .Select(index => new ProductCatalogItem
            {
                Name = $"Product {index}",
                UpdatedUtc = new DateTime(2026, 3, index, 12, 0, 0, DateTimeKind.Utc)
            })
            .ToList();

        fixture.DbContext.AddRange(domain, capability, activeComponent, deletedComponent);
        fixture.DbContext.ProductCatalogItems.AddRange(products);
        await fixture.DbContext.SaveChangesAsync();

        fixture.DbContext.ProductMappings.AddRange(
            new ProductMapping
            {
                ProductCatalogItemId = products[0].Id,
                TrmDomainId = domain.Id,
                TrmCapabilityId = capability.Id,
                TrmComponentId = activeComponent.Id,
                MappingStatus = MappingStatus.Complete
            },
            new ProductMapping
            {
                ProductCatalogItemId = products[1].Id,
                TrmDomainId = domain.Id,
                TrmCapabilityId = capability.Id,
                TrmComponentId = activeComponent.Id,
                MappingStatus = MappingStatus.Draft
            });
        await fixture.DbContext.SaveChangesAsync();

        var result = await fixture.CreateHomeController().Index();

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<HomeDashboardViewModel>(view.Model);

        Assert.Equal(7, model.ProductCount);
        Assert.Equal(1, model.CompletedMappings);
        Assert.Equal(1, model.ReferenceComponentCount);
        Assert.Equal(1, model.DomainCount);
        Assert.Equal(1, model.CapabilityCount);
        Assert.True(model.HasReferenceModel);
        Assert.Equal(6, model.RecentProducts.Count);
        Assert.Equal("Product 7", model.RecentProducts[0].Name);
        Assert.Equal("Product 2", model.RecentProducts[^1].Name);
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

        public ReportsController CreateReportsController() => new(DbContext);

        public HomeController CreateHomeController() => new(DbContext);

        public async ValueTask DisposeAsync()
        {
            await DbContext.DisposeAsync();
            await connection.DisposeAsync();
        }
    }
}
