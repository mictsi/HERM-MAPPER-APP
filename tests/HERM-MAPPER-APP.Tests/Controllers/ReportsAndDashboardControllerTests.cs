using HERMMapperApp.Controllers;
using HERMMapperApp.Data;
using HERMMapperApp.Models;
using HERMMapperApp.Services;
using HERMMapperApp.ViewModels;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using System.Text;
using Xunit;

namespace HERMMapperApp.Tests.Controllers;

public sealed class ReportsAndDashboardControllerTests
{
    [Fact]
    public async Task ReportsIndexBuildsHierarchySankeyAndLifecycleData()
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

        await fixture.DbContext.AddRangeAsync(domain, capability, component, mappedProduct, unassignedProduct, trialProduct);
        await fixture.DbContext.SaveChangesAsync();

        fixture.DbContext.ProductMappings.Add(new ProductMapping
        {
            ProductCatalogItemId = mappedProduct.Id,
            TrmDomainId = domain.Id,
            TrmCapabilityId = capability.Id,
            TrmComponentId = component.Id,
            MappingStatus = MappingStatus.Complete
        });
        await fixture.DbContext.ServiceCatalogItems.AddRangeAsync(
            new ServiceCatalogItem
            {
                Name = "Student onboarding",
                Owner = "Team Blue",
                LifecycleStatus = "Production",
                ProductConnections =
                [
                    new ServiceCatalogItemConnection
                    {
                        FromProductCatalogItemId = unassignedProduct.Id,
                        ToProductCatalogItemId = mappedProduct.Id,
                        SortOrder = 1
                    },
                    new ServiceCatalogItemConnection
                    {
                        FromProductCatalogItemId = trialProduct.Id,
                        ToProductCatalogItemId = mappedProduct.Id,
                        SortOrder = 2
                    }
                ]
            },
            new ServiceCatalogItem
            {
                Name = "Legacy support",
                Owner = "Team Red",
                LifecycleStatus = "Trial",
                ProductLinks =
                [
                    new ServiceCatalogItemProduct { ProductCatalogItemId = trialProduct.Id, SortOrder = 1 },
                    new ServiceCatalogItemProduct { ProductCatalogItemId = mappedProduct.Id, SortOrder = 2 }
                ]
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
        Assert.False(model.ExpandBrmModelReport);
        Assert.Equal("Unassigned owner", model.SelectedLifecycleOwner);
        Assert.Equal(2, model.LifecycleProductCount);
        Assert.Equal(["Unassigned owner", "Team Blue", "Team Red"], model.AvailableOwners);

        Assert.Equal(["Not set", "Trial"], model.LifecycleStatuses.Select(x => x.Label).ToArray());
        Assert.Equal([1, 1], model.LifecycleStatuses.Select(x => x.ProductCount).ToArray());
        Assert.All(model.LifecycleStatuses, row => Assert.Equal(50.0m, row.Percentage));

        Assert.Single(model.IncomingConnections);
        var incomingConnections = model.IncomingConnections[0];
        Assert.Equal(mappedProduct.Id, incomingConnections.ProductId);
        Assert.Equal("Sentinel", incomingConnections.ProductName);
        Assert.Equal(3, incomingConnections.IncomingConnectionCount);
        Assert.Equal(2, incomingConnections.ServiceCount);
        Assert.Equal("Legacy support, Student onboarding", incomingConnections.ServicePreview);
        Assert.Equal("Legacy Tool, Pilot Tool", incomingConnections.SourceProductPreview);

        Assert.Single(model.IncomingConnectionsHeatmap);
        var incomingConnectionsHeatmap = model.IncomingConnectionsHeatmap[0];
        Assert.Equal(mappedProduct.Id, incomingConnectionsHeatmap.ProductId);
        Assert.Equal("Sentinel", incomingConnectionsHeatmap.ProductName);
        Assert.Equal(3, incomingConnectionsHeatmap.IncomingConnectionCount);
        Assert.Equal(2, incomingConnectionsHeatmap.ServiceCount);

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
        Assert.Equal(1, model.ModelDiagram.DomainCount);
        Assert.Equal(1, model.ModelDiagram.CapabilityCount);
        Assert.Equal(1, model.ModelDiagram.ComponentCount);
        Assert.Equal(3, model.ModelDiagram.ProductCount);
        Assert.Equal(1, model.ModelDiagram.MappedProductCount);
        Assert.Equal(2, model.ModelDiagram.UnmappedProductCount);
        Assert.Single(model.ModelDiagram.Domains);
        Assert.Single(model.ModelDiagram.Domains[0].Capabilities);
        Assert.Single(model.ModelDiagram.Domains[0].Capabilities[0].Components);
        Assert.Single(model.ModelDiagram.Domains[0].Capabilities[0].Components[0].Products);
        Assert.Equal(["Legacy Tool", "Pilot Tool"], model.ModelDiagram.UnmappedProducts.Select(x => x.Name).ToArray());
    }

    [Fact]
    public async Task ReportsDownloadEndpointsReturnDrawIoAndArchiXml()
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
        var product = new ProductCatalogItem
        {
            Name = "Sentinel",
            Vendor = "Contoso",
            Version = "3.2",
            Owners =
            [
                new ProductCatalogItemOwner { OwnerValue = "Team Blue" }
            ]
        };

        await fixture.DbContext.AddRangeAsync(domain, capability, component, product);
        await fixture.DbContext.SaveChangesAsync();

        fixture.DbContext.ProductMappings.Add(new ProductMapping
        {
            ProductCatalogItemId = product.Id,
            TrmDomainId = domain.Id,
            TrmCapabilityId = capability.Id,
            TrmComponentId = component.Id,
            MappingStatus = MappingStatus.Complete
        });
        await fixture.DbContext.SaveChangesAsync();

        var controller = fixture.CreateReportsController();

        var drawIoResult = await controller.DownloadDrawIoAsync();
        var drawIoXml = Encoding.UTF8.GetString(drawIoResult.FileContents);
        Assert.Equal("herm-product-model.drawio", drawIoResult.FileDownloadName);
        Assert.Contains("<mxfile", drawIoXml);
        Assert.Contains("Product Model Poster", drawIoXml);
        Assert.Contains("Sentinel", drawIoXml);

        var archiResult = await controller.DownloadArchiXmlAsync();
        var archiXml = Encoding.UTF8.GetString(archiResult.FileContents);
        Assert.Equal("herm-product-model.archimate.xml", archiResult.FileDownloadName);
        Assert.Contains("<model", archiXml);
        Assert.Contains("HERM Product Model", archiXml);
        Assert.Contains("Sentinel", archiXml);
        Assert.Contains("Technology", archiXml);
        Assert.Contains("<views>", archiXml);
    }

    [Fact]
    public async Task ReportsIndexAndPosterUseSelectedBrmModel()
    {
        await using var fixture = await TestFixture.CreateAsync();

        var selectedModel = new BrmModel
        {
            Name = "Student BRM",
            Area = "Student Services",
            Status = "Production"
        };
        var otherModel = new BrmModel
        {
            Name = "Finance BRM",
            Area = "Finance",
            Status = "Draft"
        };
        var brmDomain = new BrmDomain
        {
            Code = "BD001",
            Name = "Student Lifecycle"
        };
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
        var studentArmDomain = new ArmDomain
        {
            Code = "AD001",
            Name = "Student"
        };
        var studentArmCapability = new ArmCapability
        {
            Code = "AP001",
            Name = "Recruitment",
            ParentDomain = studentArmDomain,
            ParentDomainCode = studentArmDomain.Code
        };
        var studentArmComponent = new ArmComponent
        {
            Code = "AC001",
            Name = "Applicant Portal",
            ParentCapability = studentArmCapability,
            ParentCapabilityCode = studentArmCapability.Code
        };
        var financeArmDomain = new ArmDomain
        {
            Code = "AD002",
            Name = "Finance"
        };
        var financeArmCapability = new ArmCapability
        {
            Code = "AP002",
            Name = "Billing",
            ParentDomain = financeArmDomain,
            ParentDomainCode = financeArmDomain.Code
        };
        var financeArmComponent = new ArmComponent
        {
            Code = "AC002",
            Name = "Finance Hub",
            ParentCapability = financeArmCapability,
            ParentCapabilityCode = financeArmCapability.Code
        };

        await fixture.DbContext.AddRangeAsync(
            selectedModel,
            otherModel,
            brmDomain,
            brmCapability,
            brmComponent,
            studentArmDomain,
            studentArmCapability,
            studentArmComponent,
            financeArmDomain,
            financeArmCapability,
            financeArmComponent,
            new BusinessCapabilityCatalogItem
            {
                BrmModel = selectedModel,
                Name = $"{brmComponent.Code} {brmComponent.Name}",
                Mappings =
                [
                    new BusinessCapabilityCatalogItemMapping
                    {
                        BrmComponent = brmComponent,
                        ArmComponent = studentArmComponent,
                        ArmCapability = studentArmCapability
                    }
                ]
            },
            new BusinessCapabilityCatalogItem
            {
                BrmModel = otherModel,
                Name = $"{brmComponent.Code} {brmComponent.Name}",
                Mappings =
                [
                    new BusinessCapabilityCatalogItemMapping
                    {
                        BrmComponent = brmComponent,
                        ArmComponent = financeArmComponent,
                        ArmCapability = financeArmCapability
                    }
                ]
            });
        await fixture.DbContext.SaveChangesAsync();

        var indexResult = await fixture.CreateReportsController().Index(brmModelId: selectedModel.Id, showBrmModelReport: true);

        var indexView = Assert.IsType<ViewResult>(indexResult);
        var indexModel = Assert.IsType<ReportsViewModel>(indexView.Model);
        Assert.True(indexModel.ExpandBrmModelReport);
        Assert.Equal(selectedModel.Id, indexModel.SelectedBrmModelId);
        Assert.Equal(2, indexModel.BrmModelOptions.Count);
        Assert.Equal(selectedModel.Id, indexModel.BrmModelDiagram.BrmModelId);
        Assert.Contains("Student BRM", indexModel.BrmModelDiagram.DiagramDescription);

        var mappedArmDomains = indexModel.BrmModelDiagram.Domains
            .SelectMany(domain => domain.Capabilities)
            .SelectMany(capabilityNode => capabilityNode.Components)
            .SelectMany(componentNode => componentNode.Products)
            .Select(product => product.Name)
            .ToList();

        Assert.Contains("AD001 Student", mappedArmDomains);
        Assert.DoesNotContain("AD002 Finance", mappedArmDomains);

        var posterResult = await fixture.CreateReportsController().ModelDiagram("brm", selectedModel.Id);

        var posterView = Assert.IsType<ViewResult>(posterResult);
        var posterModel = Assert.IsType<ModelDiagramReportViewModel>(posterView.Model);
        Assert.Equal(selectedModel.Id, posterModel.BrmModelId);
        Assert.Contains("Student BRM", posterModel.PosterTitle);
    }

    [Fact]
    public async Task ReportsExportMappingsCsvReturnsCompletedMappings()
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
        var completedProduct = new ProductCatalogItem { Name = "Sentinel" };
        var draftProduct = new ProductCatalogItem { Name = "Draft Tool" };

        await fixture.DbContext.AddRangeAsync(domain, capability, component, completedProduct, draftProduct);
        await fixture.DbContext.SaveChangesAsync();

        await fixture.DbContext.ProductMappings.AddRangeAsync(
            new ProductMapping
            {
                ProductCatalogItemId = completedProduct.Id,
                TrmDomainId = domain.Id,
                TrmCapabilityId = capability.Id,
                TrmComponentId = component.Id,
                MappingStatus = MappingStatus.Complete
            },
            new ProductMapping
            {
                ProductCatalogItemId = draftProduct.Id,
                TrmDomainId = domain.Id,
                TrmCapabilityId = capability.Id,
                MappingStatus = MappingStatus.Draft
            });
        await fixture.DbContext.SaveChangesAsync();

        var result = await fixture.CreateReportsController().ExportMappingsCsv();
        var file = Assert.IsType<FileContentResult>(result);

        Assert.Equal("text/csv", file.ContentType);
        var content = Encoding.UTF8.GetString(file.FileContents);
        Assert.Contains("Sentinel", content);
        Assert.DoesNotContain("Draft Tool", content);
    }

    [Fact]
    public async Task HomeIndexReturnsDashboardCountsAndRecentProducts()
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

        await fixture.DbContext.AddRangeAsync(domain, capability, activeComponent, deletedComponent);
        await fixture.DbContext.ProductCatalogItems.AddRangeAsync(products);
        await fixture.DbContext.SaveChangesAsync();

        await fixture.DbContext.ProductMappings.AddRangeAsync(
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
            Assert.Equal(1, model.TrmComponentCount);
            Assert.Equal(1, model.TrmDomainCount);
            Assert.Equal(1, model.TrmCapabilityCount);
            Assert.True(model.HasTrmModel);
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

        public ReportsController CreateReportsController() => new(
            DbContext,
            new ModelDiagramReportService(DbContext),
            new ReferenceModelDiagramService(DbContext),
            new ModelDiagramPosterSvgService(new TestWebHostEnvironment()),
            new TestWebHostEnvironment());

        public HomeController CreateHomeController() => new(DbContext);

        public async ValueTask DisposeAsync()
        {
            await DbContext.DisposeAsync();
            await connection.DisposeAsync();
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
}
