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

        var redirect = await fixture.CreateReportsController().Index("Unassigned owner");
        var redirectResult = Assert.IsType<RedirectToActionResult>(redirect);
        Assert.Equal(nameof(ReportsController.LifecycleStatusReport), redirectResult.ActionName);
        Assert.Equal("Unassigned owner", redirectResult.RouteValues?["lifecycleOwner"]);

        var result = await fixture.CreateReportsController().LifecycleStatusReport("Unassigned owner");

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
        Assert.Equal("TRM diagram (all objects)", model.ModelDiagram.DiagramTitle);
        Assert.Equal(1, model.ModelDiagram.CapabilityCount);
        Assert.Equal(1, model.ModelDiagram.ComponentCount);
        Assert.Equal(3, model.ModelDiagram.ProductCount);
        Assert.Equal(1, model.ModelDiagram.MappedProductCount);
        Assert.Equal(2, model.ModelDiagram.UnmappedProductCount);
        Assert.Equal("ARM diagram (all objects)", model.ArmModelDiagram.DiagramTitle);
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
        var unmappedCapability = new BrmCapability
        {
            Code = "BC003",
            Name = "Student Support",
            ParentDomain = brmDomain,
            ParentDomainCode = brmDomain.Code
        };
        var unmappedComponent = new BrmComponent
        {
            Code = "BC004",
            Name = "Case Guidance",
            ParentCapability = unmappedCapability,
            ParentCapabilityCode = unmappedCapability.Code
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
            unmappedCapability,
            unmappedComponent,
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

        var redirect = await fixture.CreateReportsController().Index(brmModelId: selectedModel.Id, showBrmModelReport: true);
        var redirectResult = Assert.IsType<RedirectToActionResult>(redirect);
        Assert.Equal(nameof(ReportsController.BrmModelReport), redirectResult.ActionName);
        Assert.Equal(selectedModel.Id, redirectResult.RouteValues?["brmModelId"]);

        var indexResult = await fixture.CreateReportsController().BrmModelReport(selectedModel.Id);

        var indexView = Assert.IsType<ViewResult>(indexResult);
        var indexModel = Assert.IsType<ReportsViewModel>(indexView.Model);
        Assert.False(indexModel.ExpandBrmModelReport);
        Assert.Equal(selectedModel.Id, indexModel.SelectedBrmModelId);
        Assert.Equal(2, indexModel.BrmModelOptions.Count);
        Assert.Equal(selectedModel.Id, indexModel.BrmModelDiagram.BrmModelId);
        Assert.False(indexModel.BrmModelDiagram.OnlyShowMappedNodes);
        Assert.Contains("Student BRM", indexModel.BrmModelDiagram.DiagramDescription);

        var mappedArmDomains = indexModel.BrmModelDiagram.Domains
            .SelectMany(domain => domain.Capabilities)
            .SelectMany(capabilityNode => capabilityNode.Components)
            .SelectMany(componentNode => componentNode.Products)
            .Select(product => product.Name)
            .ToList();

        Assert.Contains("AD001 Student", mappedArmDomains);
        Assert.DoesNotContain("AD002 Finance", mappedArmDomains);

        var brmComponentNames = indexModel.BrmModelDiagram.Domains
            .SelectMany(domain => domain.Capabilities)
            .SelectMany(capabilityNode => capabilityNode.Components)
            .Select(componentNode => componentNode.Name)
            .ToList();

        Assert.Contains("Student Recruitment", brmComponentNames);
        Assert.Contains("Case Guidance", brmComponentNames);

        var posterResult = await fixture.CreateReportsController().ModelDiagram("brm", selectedModel.Id);

        var posterView = Assert.IsType<ViewResult>(posterResult);
        var posterModel = Assert.IsType<ModelDiagramReportViewModel>(posterView.Model);
        Assert.Equal(selectedModel.Id, posterModel.BrmModelId);
        Assert.False(posterModel.OnlyShowMappedNodes);
        Assert.Contains("Student BRM", posterModel.PosterTitle);
    }

    [Fact]
    public async Task TrmServiceDiagramReportFiltersDiagramToSelectedService()
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
            Name = "Platforms",
            ParentDomain = domain,
            ParentDomainCode = domain.Code
        };
        var componentA = new TrmComponent
        {
            Code = "TC001",
            Name = "Identity",
            ParentCapability = capability,
            ParentCapabilityCode = capability.Code
        };
        var componentB = new TrmComponent
        {
            Code = "TC002",
            Name = "Analytics",
            ParentCapability = capability,
            ParentCapabilityCode = capability.Code
        };
        var serviceProduct = new ProductCatalogItem { Name = "Service Product" };
        var otherServiceProduct = new ProductCatalogItem { Name = "Other Service Product" };
        var unmappedServiceProduct = new ProductCatalogItem { Name = "Needs Mapping" };
        var selectedService = new ServiceCatalogItem
        {
            Name = "Student onboarding",
            Owner = "Team Blue",
            LifecycleStatus = "Production",
            ProductLinks =
            [
                new ServiceCatalogItemProduct { ProductCatalogItem = serviceProduct, SortOrder = 1 },
                new ServiceCatalogItemProduct { ProductCatalogItem = unmappedServiceProduct, SortOrder = 2 }
            ]
        };
        var otherService = new ServiceCatalogItem
        {
            Name = "Finance service",
            Owner = "Team Green",
            LifecycleStatus = "Production",
            ProductLinks =
            [
                new ServiceCatalogItemProduct { ProductCatalogItem = otherServiceProduct, SortOrder = 1 }
            ]
        };

        await fixture.DbContext.AddRangeAsync(
            domain,
            capability,
            componentA,
            componentB,
            serviceProduct,
            otherServiceProduct,
            unmappedServiceProduct,
            selectedService,
            otherService);
        await fixture.DbContext.SaveChangesAsync();

        await fixture.DbContext.ProductMappings.AddRangeAsync(
            new ProductMapping
            {
                ProductCatalogItemId = serviceProduct.Id,
                TrmDomainId = domain.Id,
                TrmCapabilityId = capability.Id,
                TrmComponentId = componentA.Id,
                MappingStatus = MappingStatus.Complete
            },
            new ProductMapping
            {
                ProductCatalogItemId = otherServiceProduct.Id,
                TrmDomainId = domain.Id,
                TrmCapabilityId = capability.Id,
                TrmComponentId = componentB.Id,
                MappingStatus = MappingStatus.Complete
            });
        await fixture.DbContext.SaveChangesAsync();

        var result = await fixture.CreateReportsController().TrmServiceDiagramReport(selectedService.Id);

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<ReportsViewModel>(view.Model);
        Assert.Equal(selectedService.Id, model.SelectedServiceId);
        Assert.Equal(2, model.ServiceOptions.Count);
        Assert.Equal("TRM diagram per service", model.TrmServiceDiagram.DiagramTitle);
        Assert.Null(model.TrmServiceDiagram.DrawIoDownloadAction);
        Assert.Contains("Student onboarding", model.TrmServiceDiagram.DiagramDescription);

        var mappedProducts = model.TrmServiceDiagram.Domains
            .SelectMany(x => x.Capabilities)
            .SelectMany(x => x.Components)
            .SelectMany(x => x.Products)
            .Select(x => x.Name)
            .ToList();

        Assert.Contains("Service Product", mappedProducts);
        Assert.DoesNotContain("Other Service Product", mappedProducts);
        Assert.Equal(["Needs Mapping"], model.TrmServiceDiagram.UnmappedProducts.Select(x => x.Name).ToArray());

        var posterResult = await fixture.CreateReportsController().ModelDiagram(scope: "trm", serviceId: selectedService.Id);

        var posterView = Assert.IsType<ViewResult>(posterResult);
        var posterModel = Assert.IsType<ModelDiagramReportViewModel>(posterView.Model);
        Assert.Equal(selectedService.Id, posterModel.ServiceId);
        Assert.Equal("TrmServiceDiagramReport", posterModel.BackReportAction);
    }

    [Fact]
    public async Task ArmApplicationDiagramReportFiltersDiagramToSelectedApplication()
    {
        await using var fixture = await TestFixture.CreateAsync();

        var armDomain = new ArmDomain
        {
            Code = "AD001",
            Name = "Business Apps"
        };
        var armCapability = new ArmCapability
        {
            Code = "AP001",
            Name = "Core Capability",
            ParentDomain = armDomain,
            ParentDomainCode = armDomain.Code
        };
        var armComponentA = new ArmComponent
        {
            Code = "AC001",
            Name = "Student Portal",
            ParentCapability = armCapability,
            ParentCapabilityCode = armCapability.Code
        };
        var armComponentB = new ArmComponent
        {
            Code = "AC002",
            Name = "Finance Portal",
            ParentCapability = armCapability,
            ParentCapabilityCode = armCapability.Code
        };
        var trmDomainA = new TrmDomain
        {
            Code = "TD001",
            Name = "Identity"
        };
        var trmCapabilityA = new TrmCapability
        {
            Code = "TP001",
            Name = "Access",
            ParentDomain = trmDomainA,
            ParentDomainCode = trmDomainA.Code
        };
        var trmComponentA = new TrmComponent
        {
            Code = "TC001",
            Name = "SSO",
            ParentCapability = trmCapabilityA,
            ParentCapabilityCode = trmCapabilityA.Code
        };
        var trmDomainB = new TrmDomain
        {
            Code = "TD002",
            Name = "Finance Tech"
        };
        var trmCapabilityB = new TrmCapability
        {
            Code = "TP002",
            Name = "Billing",
            ParentDomain = trmDomainB,
            ParentDomainCode = trmDomainB.Code
        };
        var trmComponentB = new TrmComponent
        {
            Code = "TC002",
            Name = "Payments",
            ParentCapability = trmCapabilityB,
            ParentCapabilityCode = trmCapabilityB.Code
        };
        var applicationA = new ApplicationCatalogItem { Name = "Student app" };
        var applicationB = new ApplicationCatalogItem { Name = "Finance app" };
        var productA = new ProductCatalogItem { Name = "Student Identity" };
        var productB = new ProductCatalogItem { Name = "Finance Engine" };

        await fixture.DbContext.AddRangeAsync(
            armDomain,
            armCapability,
            armComponentA,
            armComponentB,
            trmDomainA,
            trmCapabilityA,
            trmComponentA,
            trmDomainB,
            trmCapabilityB,
            trmComponentB,
            applicationA,
            applicationB,
            productA,
            productB);
        await fixture.DbContext.SaveChangesAsync();

        var productMappingA = new ProductMapping
        {
            ProductCatalogItemId = productA.Id,
            TrmDomainId = trmDomainA.Id,
            TrmCapabilityId = trmCapabilityA.Id,
            TrmComponentId = trmComponentA.Id,
            MappingStatus = MappingStatus.Complete
        };
        var productMappingB = new ProductMapping
        {
            ProductCatalogItemId = productB.Id,
            TrmDomainId = trmDomainB.Id,
            TrmCapabilityId = trmCapabilityB.Id,
            TrmComponentId = trmComponentB.Id,
            MappingStatus = MappingStatus.Complete
        };

        await fixture.DbContext.ProductMappings.AddRangeAsync(productMappingA, productMappingB);
        await fixture.DbContext.SaveChangesAsync();

        await fixture.DbContext.ApplicationCatalogItemMappings.AddRangeAsync(
            new ApplicationCatalogItemMapping
            {
                ApplicationCatalogItemId = applicationA.Id,
                ArmComponentId = armComponentA.Id,
                ProductCatalogItemId = productA.Id,
                ProductMappingId = productMappingA.Id
            },
            new ApplicationCatalogItemMapping
            {
                ApplicationCatalogItemId = applicationB.Id,
                ArmComponentId = armComponentB.Id,
                ProductCatalogItemId = productB.Id,
                ProductMappingId = productMappingB.Id
            });
        await fixture.DbContext.SaveChangesAsync();

        var result = await fixture.CreateReportsController().ArmApplicationDiagramReport(applicationA.Id);

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<ReportsViewModel>(view.Model);
        Assert.Equal(applicationA.Id, model.SelectedApplicationId);
        Assert.Equal(2, model.ApplicationOptions.Count);
        Assert.Equal("ARM diagram per application", model.ArmApplicationDiagram.DiagramTitle);
        Assert.Null(model.ArmApplicationDiagram.DrawIoDownloadAction);
        Assert.Contains("Student app", model.ArmApplicationDiagram.DiagramDescription);

        var mappedDomains = model.ArmApplicationDiagram.Domains
            .SelectMany(x => x.Capabilities)
            .SelectMany(x => x.Components)
            .SelectMany(x => x.Products)
            .Select(x => x.Name)
            .ToList();

        Assert.Contains("TD001 Identity", mappedDomains);
        Assert.DoesNotContain("TD002 Finance Tech", mappedDomains);

        var posterResult = await fixture.CreateReportsController().ModelDiagram(scope: "arm", applicationId: applicationA.Id);

        var posterView = Assert.IsType<ViewResult>(posterResult);
        var posterModel = Assert.IsType<ModelDiagramReportViewModel>(posterView.Model);
        Assert.Equal(applicationA.Id, posterModel.ApplicationId);
        Assert.Equal("ArmApplicationDiagramReport", posterModel.BackReportAction);
    }

    [Fact]
    public void ModelDiagramPosterSvgBuildsImplicitWhiteCardsForBrmPosterNodes()
    {
        var service = new ModelDiagramPosterSvgService(new TestWebHostEnvironment
        {
            ContentRootPath = ResolveRepositoryRoot()
        });

        var svg = service.BuildSvg(new ModelDiagramReportViewModel
        {
            ScopeKey = "brm"
        });

        var studentAcademicCard = GetSvgWindow(svg, "(BC046)");
        Assert.Contains("Student Academic", studentAcademicCard);
        Assert.Contains("fill=\"#ffffff\"", studentAcademicCard);
        Assert.Contains("<rect", studentAcademicCard);
        Assert.Contains("<text", studentAcademicCard);

        var housingCard = GetSvgWindow(svg, "(BC115)");
        Assert.Contains("Housing", housingCard);
        Assert.Contains("fill=\"#ffffff\"", housingCard);
        Assert.Contains("<rect", housingCard);
        Assert.Contains("<text", housingCard);
    }

    [Fact]
    public void ModelDiagramPosterSvgHighlightsMappedComponentsWithRedBorder()
    {
        var service = new ModelDiagramPosterSvgService(new TestWebHostEnvironment
        {
            ContentRootPath = ResolveRepositoryRoot()
        });

        var svg = service.BuildSvg(new ModelDiagramReportViewModel
        {
            ScopeKey = "brm",
            Domains =
            [
                new ModelDiagramDomainViewModel
                {
                    Code = "BD001",
                    Name = "Student Lifecycle",
                    Capabilities =
                    [
                        new ModelDiagramCapabilityViewModel
                        {
                            Code = "BC019",
                            Name = "Student Enrolment",
                            Components =
                            [
                                new ModelDiagramComponentViewModel
                                {
                                    Code = "BC021",
                                    Name = "Enrolment",
                                    Products =
                                    [
                                        new ModelDiagramProductViewModel
                                        {
                                            ProductId = 1,
                                            Name = "AD003 Enabling"
                                        }
                                    ]
                                }
                            ]
                        }
                    ]
                }
            ]
        });

        Assert.Contains(
            "<rect x=\"1236.218\" y=\"200.782\" width=\"177.165\" height=\"107.866\" rx=\"16\" ry=\"16\" fill=\"#ffffff\" stroke=\"#c92d39\" stroke-width=\"4\" />",
            svg);
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

    private static string ResolveRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, ".local.data", "Model", "HERM-BRM-V320-model.drawio")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the repository root for BRM poster template tests.");
    }

    private static string GetSvgWindow(string svg, string marker, int radius = 600)
    {
        var index = svg.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(index >= 0, $"Expected to find '{marker}' in the generated SVG.");

        var start = Math.Max(0, index - radius);
        var length = Math.Min(svg.Length - start, radius * 2);
        return svg.Substring(start, length);
    }
}
