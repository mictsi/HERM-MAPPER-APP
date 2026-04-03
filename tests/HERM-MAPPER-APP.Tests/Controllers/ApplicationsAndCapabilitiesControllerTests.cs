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
    public async Task CreateFormsStartWithSingleEmptyMappingRowAsync()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var seeded = await fixture.SeedHermAlignmentAsync();

        using var applicationsController = fixture.CreateApplicationsController();
        var applicationCreate = await applicationsController.CreateAsync();

        var applicationView = Assert.IsType<ViewResult>(applicationCreate);
        var applicationModel = Assert.IsType<ApplicationEditViewModel>(applicationView.Model);
        Assert.Single(applicationModel.MappingRows);

        using var capabilitiesController = fixture.CreateCapabilitiesController();
        var capabilityCreateWithoutModel = await capabilitiesController.CreateAsync();

        var capabilityRedirect = Assert.IsType<RedirectToActionResult>(capabilityCreateWithoutModel);
        Assert.Equal("Index", capabilityRedirect.ActionName);
        Assert.Equal("BrmModels", capabilityRedirect.ControllerName);

        using var scopedCapabilitiesController = fixture.CreateCapabilitiesController();
        var capabilityCreate = await scopedCapabilitiesController.CreateAsync(seeded.BrmModel.Id);

        var capabilityView = Assert.IsType<ViewResult>(capabilityCreate);
        var capabilityModel = Assert.IsType<CapabilityEditViewModel>(capabilityView.Model);
        Assert.Single(capabilityModel.MappingRows);
        Assert.Equal(seeded.BrmModel.Id, capabilityModel.SelectedBrmModelId);
        Assert.Equal("Student BRM", capabilityModel.BrmModelName);
        Assert.Equal("Student Services", capabilityModel.BrmModelArea);
        Assert.Equal("Production", capabilityModel.BrmModelStatus);
    }

    [Fact]
    public async Task ApplicationIndexSearchMatchesMappedProductAndTrmLabelsAsync()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var seeded = await fixture.SeedHermAlignmentAsync();

        await fixture.DbContext.AddAsync(new ApplicationCatalogItem
        {
            Name = "Admissions Hub",
            Mappings =
            [
                new ApplicationCatalogItemMapping
                {
                    ArmComponentId = seeded.ArmComponent.Id,
                    ProductMappingId = seeded.ProductMapping.Id,
                    ProductCatalogItemId = seeded.Product.Id
                }
            ]
        });
        await fixture.DbContext.SaveChangesAsync();

        using var controller = fixture.CreateApplicationsController();
        var result = await controller.IndexAsync("Integration Platform");

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<ApplicationsIndexViewModel>(view.Model);
        var row = Assert.Single(model.Applications);
        Assert.Equal("Admissions Hub", row.Name);
        Assert.Equal(1, row.ProductCount);
    }

    [Fact]
    public async Task ApplicationDetailsReturnsBadRequestWhenModelStateIsInvalidAsync()
    {
        await using var fixture = await TestFixture.CreateAsync();
        using var controller = fixture.CreateApplicationsController();
        controller.ModelState.AddModelError("id", "invalid");

        var result = await controller.DetailsAsync(42);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task ApplicationEditReturnsBadRequestWhenModelStateIsInvalidAsync()
    {
        await using var fixture = await TestFixture.CreateAsync();
        using var controller = fixture.CreateApplicationsController();
        controller.ModelState.AddModelError("id", "invalid");

        var result = await controller.EditAsync(42);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task AllDependenciesPagesReturnSharedHierarchyRootsAsync()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var seeded = await fixture.SeedHermAlignmentAsync();

        await fixture.DbContext.AddAsync(new ApplicationCatalogItem
        {
            Name = "Admissions Hub",
            Mappings =
            [
                new ApplicationCatalogItemMapping
                {
                    ArmComponentId = seeded.ArmComponent.Id,
                    ProductMappingId = seeded.ProductMapping.Id,
                    ProductCatalogItemId = seeded.Product.Id
                }
            ]
        });

        await fixture.DbContext.AddAsync(new BusinessCapabilityCatalogItem
        {
            BrmModelId = seeded.BrmModel.Id,
            Name = $"{seeded.BrmComponent.Code} {seeded.BrmComponent.Name}",
            Mappings =
            [
                new BusinessCapabilityCatalogItemMapping
                {
                    BrmComponentId = seeded.BrmComponent.Id,
                    ArmComponentId = seeded.ArmComponent.Id,
                    ArmCapabilityId = seeded.ArmCapability.Id
                }
            ]
        });

        await fixture.DbContext.SaveChangesAsync();

        using var applicationsController = fixture.CreateApplicationsController();
        var applicationsResult = await applicationsController.AllDependenciesAsync(default);

        var applicationsView = Assert.IsType<ViewResult>(applicationsResult);
        Assert.Equal("~/Views/Shared/HierarchyDiagramPage.cshtml", applicationsView.ViewName);
        var applicationsModel = Assert.IsType<HierarchyDiagramPageViewModel>(applicationsView.Model);
        Assert.Equal("All applications", applicationsModel.HierarchyRoot.Label);
        Assert.Single(applicationsModel.HierarchyRoot.Children);
        Assert.Equal("Admissions Hub", applicationsModel.HierarchyRoot.Children[0].Label);

        using var capabilitiesController = fixture.CreateCapabilitiesController();
        var capabilitiesResult = await capabilitiesController.AllDependenciesAsync(default);

        var capabilitiesView = Assert.IsType<ViewResult>(capabilitiesResult);
        Assert.Equal("~/Views/Shared/HierarchyDiagramPage.cshtml", capabilitiesView.ViewName);
        var capabilitiesModel = Assert.IsType<HierarchyDiagramPageViewModel>(capabilitiesView.Model);
        Assert.Equal("All capabilities", capabilitiesModel.HierarchyRoot.Label);
        Assert.True(capabilitiesModel.IncludeProducts);
        Assert.Single(capabilitiesModel.HierarchyRoot.Children);
        Assert.Equal($"{seeded.BrmComponent.Code} {seeded.BrmComponent.Name}", capabilitiesModel.HierarchyRoot.Children[0].Label);
    }

    [Fact]
    public async Task ApplicationCreatePersistsExactProductMappingAndDetailsResolveDerivedParentsAsync()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var seeded = await fixture.SeedHermAlignmentAsync();

        using var controller = fixture.CreateApplicationsController();
        var createResult = await controller.CreateAsync(new ApplicationEditViewModel
        {
            Name = "Admissions Hub",
            Description = "Applicant workflow",
            MappingRows =
            [
                new ApplicationMappingRowInputViewModel
                {
                    ArmComponentId = seeded.ArmComponent.Id,
                    ProductCatalogItemId = seeded.Product.Id
                }
            ]
        });

        var redirect = Assert.IsType<RedirectToActionResult>(createResult);
        Assert.Equal("Details", redirect.ActionName);

        var application = await fixture.DbContext.ApplicationCatalogItems
            .Include(x => x.Mappings)
            .SingleAsync();
        var storedMapping = Assert.Single(application.Mappings);
        Assert.Equal(seeded.ArmComponent.Id, storedMapping.ArmComponentId);
        Assert.Equal(seeded.ProductMapping.Id, storedMapping.ProductMappingId);
        Assert.Equal(seeded.Product.Id, storedMapping.ProductCatalogItemId);

        using var detailsController = fixture.CreateApplicationsController();
        var detailsResult = await detailsController.DetailsAsync(application.Id);

        var view = Assert.IsType<ViewResult>(detailsResult);
        var model = Assert.IsType<ApplicationDetailsViewModel>(view.Model);
        var summaryRow = Assert.Single(model.MappingRows);
        Assert.Equal("AD001 Student", summaryRow.ArmDomainLabel);
        Assert.Equal("AP001 Recruitment", summaryRow.ArmCapabilityLabel);
        Assert.Equal("AC001 Applicant Portal", summaryRow.ArmComponentLabel);
        Assert.Equal("TD001 Integration", summaryRow.TrmDomainLabel);
        Assert.Equal("TP001 API and Messaging", summaryRow.TrmCapabilityLabel);
        Assert.Equal("TC001 Integration Platform", summaryRow.TrmComponentLabel);
        Assert.Equal("Contoso Platform (Contoso)", summaryRow.ProductLabel);

        var armDomainNode = Assert.Single(model.HierarchyRoot.Children);
        Assert.Equal("ARM domain", armDomainNode.NodeType);
        Assert.Equal("AD001 Student", armDomainNode.Label);

        var armCapabilityNode = Assert.Single(armDomainNode.Children);
        Assert.Equal("AP001 Recruitment", armCapabilityNode.Label);

        var armComponentNode = Assert.Single(armCapabilityNode.Children);
        Assert.Equal("AC001 Applicant Portal", armComponentNode.Label);

        var trmDomainNode = Assert.Single(armComponentNode.Children);
        Assert.Equal("TD001 Integration", trmDomainNode.Label);

        var trmCapabilityNode = Assert.Single(trmDomainNode.Children);
        Assert.Equal("TP001 API and Messaging", trmCapabilityNode.Label);

        var trmComponentNode = Assert.Single(trmCapabilityNode.Children);
        Assert.Equal("TC001 Integration Platform", trmComponentNode.Label);

        var productNode = Assert.Single(trmComponentNode.Children);
        Assert.Equal("Contoso Platform (Contoso)", productNode.Label);

        Assert.Equal(7, model.GraphConnections.Count);
        Assert.Contains(model.GraphConnections, connection =>
            connection.FromName == "Admissions Hub" &&
            connection.ToName == "AD001 Student");
        Assert.Contains(model.GraphConnections, connection =>
            connection.FromName == "TC001 Integration Platform" &&
            connection.ToName == "Contoso Platform (Contoso)");

        var path = Assert.Single(model.ResolvedPaths);
        Assert.Equal("AC001 Applicant Portal", path.ArmComponentLabel);
        Assert.Equal("TC001 Integration Platform", path.TrmComponentLabel);
        Assert.Equal("Complete", path.MappingStatus);
    }

    [Fact]
    public async Task ApplicationDetailsGraphConnectionsDeduplicateSharedDownstreamNodesAsync()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var seeded = await fixture.SeedHermAlignmentAsync();
        var armCapability = await fixture.DbContext.ArmCapabilities.SingleAsync();
        var secondArmComponent = new ArmComponent
        {
            Code = "AC002",
            Name = "Recruitment Workflow",
            ParentCapability = armCapability,
            ParentCapabilityCode = armCapability.Code
        };

        await fixture.DbContext.AddAsync(secondArmComponent);

        var application = new ApplicationCatalogItem
        {
            Name = "Admissions Hub",
            Mappings =
            [
                new ApplicationCatalogItemMapping
                {
                    ArmComponentId = seeded.ArmComponent.Id,
                    ProductMappingId = seeded.ProductMapping.Id,
                    ProductCatalogItemId = seeded.Product.Id
                },
                new ApplicationCatalogItemMapping
                {
                    ArmComponent = secondArmComponent,
                    ProductMappingId = seeded.ProductMapping.Id,
                    ProductCatalogItemId = seeded.Product.Id
                }
            ]
        };

        await fixture.DbContext.AddAsync(application);
        await fixture.DbContext.SaveChangesAsync();

        using var controller = fixture.CreateApplicationsController();
        var result = await controller.DetailsAsync(application.Id);

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<ApplicationDetailsViewModel>(view.Model);

        Assert.Equal(9, model.GraphConnections.Count);
        Assert.Equal(1, model.GraphConnections.Count(connection =>
            connection.FromName == "TD001 Integration" &&
            connection.ToName == "TP001 API and Messaging"));
        Assert.Equal(1, model.GraphConnections.Count(connection =>
            connection.FromName == "TP001 API and Messaging" &&
            connection.ToName == "TC001 Integration Platform"));
        Assert.Equal(1, model.GraphConnections.Count(connection =>
            connection.FromName == "TC001 Integration Platform" &&
            connection.ToName == "Contoso Platform (Contoso)"));
    }

    [Fact]
    public async Task ApplicationCreateRequiresTrmComponentWhenSelectedProductMapsToMultipleComponentsAsync()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var seeded = await fixture.SeedHermAlignmentAsync();
        var trmCapability = await fixture.DbContext.TrmCapabilities.SingleAsync();
        var extraTrmComponent = new TrmComponent
        {
            Code = "TC002",
            Name = "Identity Broker",
            ParentCapability = trmCapability,
            ParentCapabilityCode = trmCapability.Code
        };

        await fixture.DbContext.AddAsync(extraTrmComponent);
        await fixture.DbContext.AddAsync(new ProductMapping
        {
            ProductCatalogItemId = seeded.Product.Id,
            TrmCapabilityId = trmCapability.Id,
            TrmComponent = extraTrmComponent,
            MappingStatus = MappingStatus.Complete
        });
        await fixture.DbContext.SaveChangesAsync();

        using var controller = fixture.CreateApplicationsController();
        var result = await controller.CreateAsync(new ApplicationEditViewModel
        {
            Name = "Admissions Hub",
            MappingRows =
            [
                new ApplicationMappingRowInputViewModel
                {
                    ArmComponentId = seeded.ArmComponent.Id,
                    ProductCatalogItemId = seeded.Product.Id
                }
            ]
        });

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<ApplicationEditViewModel>(view.Model);
        Assert.False(controller.ModelState.IsValid);
        Assert.Contains(controller.ModelState, entry => entry.Key == "MappingRows[0].TrmComponentId");
        Assert.Single(model.MappingRows);
    }

    [Fact]
    public async Task ApplicationDeleteConfirmedSoftDeletesApplicationAndWritesAuditAsync()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var seeded = await fixture.SeedHermAlignmentAsync();

        await fixture.DbContext.AddAsync(new ApplicationCatalogItem
        {
            Name = "Admissions Hub",
            Mappings =
            [
                new ApplicationCatalogItemMapping
                {
                    ArmComponentId = seeded.ArmComponent.Id,
                    ProductMappingId = seeded.ProductMapping.Id,
                    ProductCatalogItemId = seeded.Product.Id
                }
            ]
        });
        await fixture.DbContext.SaveChangesAsync();

        var applicationId = await fixture.DbContext.ApplicationCatalogItems.Select(x => x.Id).SingleAsync();

        using var controller = fixture.CreateApplicationsController();
        var result = await controller.DeleteConfirmedAsync(applicationId);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Index", redirect.ActionName);

        var application = await fixture.DbContext.ApplicationCatalogItems.SingleAsync();
        Assert.True(application.IsDeleted);
        Assert.NotNull(application.DeletedUtc);
        Assert.Equal("Moved to trash from the application catalogue.", application.DeletedReason);
        Assert.Equal("Delete", (await fixture.DbContext.AuditLogEntries.SingleAsync()).Action);
    }

    [Fact]
    public async Task ApplicationRestoreDeletedClearsSoftDeleteStateAndWritesAuditAsync()
    {
        await using var fixture = await TestFixture.CreateAsync();

        await fixture.DbContext.AddAsync(new ApplicationCatalogItem
        {
            Name = "Admissions Hub",
            IsDeleted = true,
            DeletedUtc = DateTime.UtcNow.AddDays(-1),
            DeletedReason = "Moved to trash from the application catalogue."
        });
        await fixture.DbContext.SaveChangesAsync();

        var applicationId = await fixture.DbContext.ApplicationCatalogItems.Select(x => x.Id).SingleAsync();

        using var controller = fixture.CreateApplicationsController();
        var result = await controller.RestoreDeletedAsync(applicationId);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Restore", redirect.ActionName);

        var application = await fixture.DbContext.ApplicationCatalogItems.SingleAsync();
        Assert.False(application.IsDeleted);
        Assert.Null(application.DeletedUtc);
        Assert.Null(application.DeletedReason);
        Assert.Equal("Restore", (await fixture.DbContext.AuditLogEntries.SingleAsync()).Action);
    }

    [Fact]
    public async Task ApplicationIndexExcludesDeletedApplicationsAsync()
    {
        await using var fixture = await TestFixture.CreateAsync();

        await fixture.DbContext.AddRangeAsync(
            new ApplicationCatalogItem
            {
                Name = "Active App"
            },
            new ApplicationCatalogItem
            {
                Name = "Deleted App",
                IsDeleted = true,
                DeletedUtc = DateTime.UtcNow,
                DeletedReason = "Moved to trash from the application catalogue."
            });
        await fixture.DbContext.SaveChangesAsync();

        using var controller = fixture.CreateApplicationsController();
        var result = await controller.IndexAsync(null);

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<ApplicationsIndexViewModel>(view.Model);
        var row = Assert.Single(model.Applications);
        Assert.Equal("Active App", row.Name);
    }

    [Fact]
    public async Task ApplicationDetailsReturnsNotFoundWhenApplicationDeletedAsync()
    {
        await using var fixture = await TestFixture.CreateAsync();

        await fixture.DbContext.AddAsync(new ApplicationCatalogItem
        {
            Name = "Admissions Hub",
            IsDeleted = true,
            DeletedUtc = DateTime.UtcNow,
            DeletedReason = "Moved to trash from the application catalogue."
        });
        await fixture.DbContext.SaveChangesAsync();

        var applicationId = await fixture.DbContext.ApplicationCatalogItems.Select(x => x.Id).SingleAsync();

        using var controller = fixture.CreateApplicationsController();
        var result = await controller.DetailsAsync(applicationId);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task CapabilityCreateUsesSelectedBrmComponentAndDetailsResolveApplicationsProductsAndTrmPathsAsync()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var seeded = await fixture.SeedHermAlignmentAsync();

        var application = new ApplicationCatalogItem
        {
            Name = "Admissions Hub",
            Mappings =
            [
                new ApplicationCatalogItemMapping
                {
                    ArmComponentId = seeded.ArmComponent.Id,
                    ProductMappingId = seeded.ProductMapping.Id,
                    ProductCatalogItemId = seeded.Product.Id
                }
            ]
        };

        await fixture.DbContext.AddAsync(application);
        await fixture.DbContext.SaveChangesAsync();

        using var controller = fixture.CreateCapabilitiesController();
        var createResult = await controller.CreateAsync(seeded.BrmModel.Id, new CapabilityEditViewModel
        {
            SelectedBrmModelId = seeded.BrmModel.Id,
            SelectedBrmComponentId = seeded.BrmComponent.Id,
            Description = "Business recruitment view",
            MappingRows =
            [
                new CapabilityMappingRowInputViewModel
                {
                    ArmComponentId = seeded.ArmComponent.Id,
                    ArmCapabilityId = seeded.ArmCapability.Id
                }
            ]
        });

        var redirect = Assert.IsType<RedirectToActionResult>(createResult);
        Assert.Equal("Details", redirect.ActionName);
        Assert.Equal("BrmModels", redirect.ControllerName);
        Assert.Equal(seeded.BrmModel.Id, redirect.RouteValues?["id"]);

        var capability = await fixture.DbContext.BusinessCapabilityCatalogItems
            .Include(x => x.Mappings)
            .SingleAsync();
        Assert.Equal(seeded.BrmModel.Id, capability.BrmModelId);
        Assert.Equal("BC002 Student Recruitment", capability.Name);
        var storedMapping = Assert.Single(capability.Mappings);
        Assert.Equal(seeded.BrmComponent.Id, storedMapping.BrmComponentId);
        Assert.Equal(seeded.ArmComponent.Id, storedMapping.ArmComponentId);
        Assert.Equal(seeded.ArmCapability.Id, storedMapping.ArmCapabilityId);

        using var detailsController = fixture.CreateCapabilitiesController();
        var result = await detailsController.DetailsAsync(capability.Id);

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<CapabilityDetailsViewModel>(view.Model);
        Assert.Equal("Student BRM", model.BrmModelName);
        Assert.Equal("Student Services", model.BrmModelArea);
        Assert.Equal("Production", model.BrmModelStatus);
        var mappingRow = Assert.Single(model.MappingRows);
        Assert.Equal("BD001 Student Lifecycle", mappingRow.BrmDomainLabel);
        Assert.Equal("BC001 Student Management", mappingRow.BrmCapabilityLabel);
        Assert.Equal("BC002 Student Recruitment", mappingRow.BrmComponentLabel);
        Assert.Equal("AD001 Student", mappingRow.ArmDomainLabel);
        Assert.Equal("AP001 Recruitment", mappingRow.ArmCapabilityLabel);
        Assert.Equal("AC001 Applicant Portal", mappingRow.ArmComponentLabel);

        var path = Assert.Single(model.ResolvedPaths);
        Assert.Equal("BC002 Student Recruitment", path.BrmComponentLabel);
        Assert.Equal("AC001 Applicant Portal", path.ArmComponentLabel);
        Assert.Equal("Admissions Hub", path.ApplicationName);
        Assert.Equal("Contoso Platform (Contoso)", path.ProductLabel);
        Assert.Equal("TC001 Integration Platform", path.TrmComponentLabel);

        var brmDomainNode = Assert.Single(model.HierarchyRoot.Children);
        Assert.Equal("BRM domain", brmDomainNode.NodeType);
        Assert.Equal("BD001 Student Lifecycle", brmDomainNode.Label);

        var brmCapabilityNode = Assert.Single(brmDomainNode.Children);
        Assert.Equal("BC001 Student Management", brmCapabilityNode.Label);

        var brmComponentNode = Assert.Single(brmCapabilityNode.Children);
        Assert.Equal("BC002 Student Recruitment", brmComponentNode.Label);

        var armDomainNode = Assert.Single(brmComponentNode.Children);
        Assert.Equal("AD001 Student", armDomainNode.Label);

        var applicationNode = Assert.Single(Assert.Single(Assert.Single(armDomainNode.Children).Children).Children);
        Assert.Equal("Admissions Hub", applicationNode.Label);
    }

    [Fact]
    public async Task CapabilityCreateRedirectsWhenBrmModelDeletedAsync()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var seeded = await fixture.SeedHermAlignmentAsync();

        seeded.BrmModel.IsDeleted = true;
        seeded.BrmModel.DeletedUtc = DateTime.UtcNow;
        seeded.BrmModel.DeletedReason = "Moved to trash from the BRM model catalogue.";
        await fixture.DbContext.SaveChangesAsync();

        using var controller = fixture.CreateCapabilitiesController();
        var result = await controller.CreateAsync(seeded.BrmModel.Id);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Index", redirect.ActionName);
        Assert.Equal("BrmModels", redirect.ControllerName);
    }

    [Fact]
    public async Task CapabilityHierarchyCountsDistinctProductsInsteadOfApplicationsAsync()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var seeded = await fixture.SeedHermAlignmentAsync();

        await fixture.DbContext.AddRangeAsync(
            new ApplicationCatalogItem
            {
                Name = "Admissions Hub",
                Mappings =
                [
                    new ApplicationCatalogItemMapping
                    {
                        ArmComponentId = seeded.ArmComponent.Id,
                        ProductMappingId = seeded.ProductMapping.Id,
                        ProductCatalogItemId = seeded.Product.Id
                    }
                ]
            },
            new ApplicationCatalogItem
            {
                Name = "Enrolment Portal",
                Mappings =
                [
                    new ApplicationCatalogItemMapping
                    {
                        ArmComponentId = seeded.ArmComponent.Id,
                        ProductMappingId = seeded.ProductMapping.Id,
                        ProductCatalogItemId = seeded.Product.Id
                    }
                ]
            },
            new BusinessCapabilityCatalogItem
            {
                BrmModelId = seeded.BrmModel.Id,
                Name = $"{seeded.BrmComponent.Code} {seeded.BrmComponent.Name}",
                Mappings =
                [
                    new BusinessCapabilityCatalogItemMapping
                    {
                        BrmComponentId = seeded.BrmComponent.Id,
                        ArmComponentId = seeded.ArmComponent.Id,
                        ArmCapabilityId = seeded.ArmCapability.Id
                    }
                ]
            });
        await fixture.DbContext.SaveChangesAsync();

        var capability = await fixture.DbContext.BusinessCapabilityCatalogItems.SingleAsync();

        using var controller = fixture.CreateCapabilitiesController();
        var result = await controller.DetailsAsync(capability.Id);

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<CapabilityDetailsViewModel>(view.Model);

        Assert.Equal(2, model.ApplicationCount);
        Assert.Equal(1, model.ProductCount);
        Assert.Equal(1, model.HierarchyRoot.ProductCount);

        var brmDomainNode = Assert.Single(model.HierarchyRoot.Children);
        Assert.Equal(1, brmDomainNode.ProductCount);
        var brmCapabilityNode = Assert.Single(brmDomainNode.Children);
        Assert.Equal(1, brmCapabilityNode.ProductCount);
    }

    [Fact]
    public async Task CapabilitiesIndexRedirectsToBrmModelsIndexAsync()
    {
        await using var fixture = await TestFixture.CreateAsync();
        await fixture.SeedHermAlignmentAsync();

        using var controller = fixture.CreateCapabilitiesController();
        var result = await controller.IndexAsync("Applicant Portal");

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Index", redirect.ActionName);
        Assert.Equal("BrmModels", redirect.ControllerName);
    }

    [Fact]
    public async Task CapabilityDetailsReturnsBadRequestWhenModelStateIsInvalidAsync()
    {
        await using var fixture = await TestFixture.CreateAsync();
        using var controller = fixture.CreateCapabilitiesController();
        controller.ModelState.AddModelError("id", "invalid");

        var result = await controller.DetailsAsync(42);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task CapabilityEditReturnsBadRequestWhenModelStateIsInvalidAsync()
    {
        await using var fixture = await TestFixture.CreateAsync();
        using var controller = fixture.CreateCapabilitiesController();
        controller.ModelState.AddModelError("id", "invalid");

        var result = await controller.EditAsync(42);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task CapabilityCreateRequiresCapabilitySelectionWhenArmComponentHasMultipleCapabilityConnectionsAsync()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var seeded = await fixture.SeedHermAlignmentAsync();
        var secondArmCapability = new ArmCapability
        {
            Code = "AP002",
            Name = "Operations",
            ParentDomain = await fixture.DbContext.ArmDomains.SingleAsync(),
            ParentDomainCode = "AD001"
        };

        await fixture.DbContext.AddAsync(secondArmCapability);
        await fixture.DbContext.SaveChangesAsync();

        await fixture.DbContext.AddAsync(new ArmComponentCapabilityLink
        {
            ArmComponentId = seeded.ArmComponent.Id,
            ArmCapabilityId = secondArmCapability.Id
        });
        await fixture.DbContext.SaveChangesAsync();

        using var controller = fixture.CreateCapabilitiesController();
        var invalidResult = await controller.CreateAsync(seeded.BrmModel.Id, new CapabilityEditViewModel
        {
            SelectedBrmModelId = seeded.BrmModel.Id,
            SelectedBrmComponentId = seeded.BrmComponent.Id,
            MappingRows =
            [
                new CapabilityMappingRowInputViewModel
                {
                    ArmComponentId = seeded.ArmComponent.Id
                }
            ]
        });

        var invalidView = Assert.IsType<ViewResult>(invalidResult);
        var invalidModel = Assert.IsType<CapabilityEditViewModel>(invalidView.Model);
        Assert.False(controller.ModelState.IsValid);
        Assert.Contains(controller.ModelState, entry => entry.Key == "MappingRows[0].ArmCapabilityId");
        Assert.Single(invalidModel.MappingRows);

        using var validController = fixture.CreateCapabilitiesController();
        var validResult = await validController.CreateAsync(seeded.BrmModel.Id, new CapabilityEditViewModel
        {
            SelectedBrmModelId = seeded.BrmModel.Id,
            SelectedBrmComponentId = seeded.BrmComponent.Id,
            MappingRows =
            [
                new CapabilityMappingRowInputViewModel
                {
                    ArmComponentId = seeded.ArmComponent.Id,
                    ArmCapabilityId = secondArmCapability.Id
                }
            ]
        });

        var redirect = Assert.IsType<RedirectToActionResult>(validResult);
        Assert.Equal("Details", redirect.ActionName);
        Assert.Equal("BrmModels", redirect.ControllerName);
        Assert.Equal(seeded.BrmModel.Id, redirect.RouteValues?["id"]);

        var mapping = await fixture.DbContext.BusinessCapabilityCatalogItemMappings.SingleAsync();
        Assert.Equal(seeded.ArmComponent.Id, mapping.ArmComponentId);
        Assert.Equal(secondArmCapability.Id, mapping.ArmCapabilityId);
    }

    [Fact]
    public async Task CapabilityCreateUsesRouteBrmModelInsteadOfPostedModelSelectionAsync()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var seeded = await fixture.SeedHermAlignmentAsync();
        var otherModel = new BrmModel
        {
            Name = "Operations BRM",
            Area = "Operations",
            Status = "Draft"
        };

        await fixture.DbContext.AddAsync(otherModel);
        await fixture.DbContext.SaveChangesAsync();

        using var controller = fixture.CreateCapabilitiesController();
        var result = await controller.CreateAsync(seeded.BrmModel.Id, new CapabilityEditViewModel
        {
            SelectedBrmModelId = otherModel.Id,
            SelectedBrmComponentId = seeded.BrmComponent.Id,
            MappingRows =
            [
                new CapabilityMappingRowInputViewModel
                {
                    ArmComponentId = seeded.ArmComponent.Id,
                    ArmCapabilityId = seeded.ArmCapability.Id
                }
            ]
        });

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Details", redirect.ActionName);
        Assert.Equal(seeded.BrmModel.Id, redirect.RouteValues?["id"]);

        var capability = await fixture.DbContext.BusinessCapabilityCatalogItems.SingleAsync();
        Assert.Equal(seeded.BrmModel.Id, capability.BrmModelId);
    }

    [Fact]
    public async Task CapabilityEditKeepsExistingBrmModelEvenIfPostedModelChangesAsync()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var seeded = await fixture.SeedHermAlignmentAsync();
        var otherModel = new BrmModel
        {
            Name = "Operations BRM",
            Area = "Operations",
            Status = "Draft"
        };

        await fixture.DbContext.AddAsync(otherModel);
        await fixture.DbContext.AddAsync(new BusinessCapabilityCatalogItem
        {
            BrmModelId = seeded.BrmModel.Id,
            Name = $"{seeded.BrmComponent.Code} {seeded.BrmComponent.Name}",
            Mappings =
            [
                new BusinessCapabilityCatalogItemMapping
                {
                    BrmComponentId = seeded.BrmComponent.Id,
                    ArmComponentId = seeded.ArmComponent.Id,
                    ArmCapabilityId = seeded.ArmCapability.Id
                }
            ]
        });
        await fixture.DbContext.SaveChangesAsync();

        var capability = await fixture.DbContext.BusinessCapabilityCatalogItems.SingleAsync();

        using var controller = fixture.CreateCapabilitiesController();
        var result = await controller.EditAsync(capability.Id, new CapabilityEditViewModel
        {
            SelectedBrmModelId = otherModel.Id,
            SelectedBrmComponentId = seeded.BrmComponent.Id,
            Description = "Updated description",
            MappingRows =
            [
                new CapabilityMappingRowInputViewModel
                {
                    ArmComponentId = seeded.ArmComponent.Id,
                    ArmCapabilityId = seeded.ArmCapability.Id
                }
            ]
        });

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Details", redirect.ActionName);
        Assert.Equal(seeded.BrmModel.Id, redirect.RouteValues?["id"]);

        var storedCapability = await fixture.DbContext.BusinessCapabilityCatalogItems.SingleAsync();
        Assert.Equal(seeded.BrmModel.Id, storedCapability.BrmModelId);
        Assert.Equal("Updated description", storedCapability.Description);
    }

    [Fact]
    public async Task CapabilityDeleteRemovesCapabilityAndRedirectsToOwningBrmModelAsync()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var seeded = await fixture.SeedHermAlignmentAsync();

        await fixture.DbContext.AddAsync(new BusinessCapabilityCatalogItem
        {
            BrmModelId = seeded.BrmModel.Id,
            Name = $"{seeded.BrmComponent.Code} {seeded.BrmComponent.Name}",
            Mappings =
            [
                new BusinessCapabilityCatalogItemMapping
                {
                    BrmComponentId = seeded.BrmComponent.Id,
                    ArmComponentId = seeded.ArmComponent.Id,
                    ArmCapabilityId = seeded.ArmCapability.Id
                }
            ]
        });
        await fixture.DbContext.SaveChangesAsync();

        var capability = await fixture.DbContext.BusinessCapabilityCatalogItems.SingleAsync();

        using var controller = fixture.CreateCapabilitiesController();
        var result = await controller.DeleteConfirmedAsync(capability.Id);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Details", redirect.ActionName);
        Assert.Equal("BrmModels", redirect.ControllerName);
        Assert.Equal(seeded.BrmModel.Id, redirect.RouteValues?["id"]);
        Assert.False(await fixture.DbContext.BusinessCapabilityCatalogItems.AnyAsync());
        Assert.False(await fixture.DbContext.BusinessCapabilityCatalogItemMappings.AnyAsync());
    }

    [Fact]
    public async Task CapabilitiesIndexWithBrmModelIdRedirectsToThatModelAsync()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var seeded = await fixture.SeedHermAlignmentAsync();

        using var controller = fixture.CreateCapabilitiesController();
        var result = await controller.IndexAsync(null, seeded.BrmModel.Id);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Details", redirect.ActionName);
        Assert.Equal("BrmModels", redirect.ControllerName);
        Assert.Equal(seeded.BrmModel.Id, redirect.RouteValues?["id"]);
    }

    [Fact]
    public async Task BrmModelCreatePersistsAndDetailsShowScopedCapabilitiesAsync()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var seeded = await fixture.SeedHermAlignmentAsync();

        using var createController = fixture.CreateBrmModelsController();
        var createResult = await createController.CreateAsync(new BrmModelEditViewModel
        {
            Name = "Operations BRM",
            Area = "Operations",
            Status = "Proposal",
            Description = "Operations capability set"
        });

        var redirect = Assert.IsType<RedirectToActionResult>(createResult);
        Assert.Equal("Details", redirect.ActionName);

        var brmModel = await fixture.DbContext.BrmModels
            .OrderByDescending(x => x.Id)
            .FirstAsync();

        await fixture.DbContext.AddAsync(new BusinessCapabilityCatalogItem
        {
            BrmModelId = brmModel.Id,
            Name = $"{seeded.BrmComponent.Code} {seeded.BrmComponent.Name}",
            Mappings =
            [
                new BusinessCapabilityCatalogItemMapping
                {
                    BrmComponentId = seeded.BrmComponent.Id,
                    ArmComponentId = seeded.ArmComponent.Id,
                    ArmCapabilityId = seeded.ArmCapability.Id
                }
            ]
        });
        await fixture.DbContext.SaveChangesAsync();

        using var detailsController = fixture.CreateBrmModelsController();
        var detailsResult = await detailsController.DetailsAsync(brmModel.Id);

        var view = Assert.IsType<ViewResult>(detailsResult);
        var model = Assert.IsType<BrmModelDetailsViewModel>(view.Model);
        Assert.Equal("Operations BRM", model.Name);
        Assert.Equal("Operations", model.Area);
        Assert.Equal("Proposal", model.Status);
        Assert.Single(model.Capabilities);
        Assert.Equal($"{seeded.BrmComponent.Code} {seeded.BrmComponent.Name}", model.Capabilities[0].Name);
    }

    [Fact]
    public async Task BrmModelDetailsBuildDependencyHierarchyForConnectedCapabilitiesAsync()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var seeded = await fixture.SeedHermAlignmentAsync();

        await fixture.DbContext.AddRangeAsync(
            new ApplicationCatalogItem
            {
                Name = "Admissions Hub",
                Mappings =
                [
                    new ApplicationCatalogItemMapping
                    {
                        ArmComponentId = seeded.ArmComponent.Id,
                        ProductMappingId = seeded.ProductMapping.Id,
                        ProductCatalogItemId = seeded.Product.Id
                    }
                ]
            },
            new BusinessCapabilityCatalogItem
            {
                BrmModelId = seeded.BrmModel.Id,
                Name = $"{seeded.BrmComponent.Code} {seeded.BrmComponent.Name}",
                Mappings =
                [
                    new BusinessCapabilityCatalogItemMapping
                    {
                        BrmComponentId = seeded.BrmComponent.Id,
                        ArmComponentId = seeded.ArmComponent.Id,
                        ArmCapabilityId = seeded.ArmCapability.Id
                    }
                ]
            });
        await fixture.DbContext.SaveChangesAsync();

        using var controller = fixture.CreateBrmModelsController();
        var result = await controller.DetailsAsync(seeded.BrmModel.Id);

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<BrmModelDetailsViewModel>(view.Model);
        Assert.True(model.HasDependencyTree);
        Assert.Equal(seeded.BrmModel.Name, model.HierarchyRoot.Label);

        var capabilityNode = Assert.Single(model.HierarchyRoot.Children);
        Assert.Equal("Capability", capabilityNode.NodeType);
        Assert.Equal($"{seeded.BrmComponent.Code} {seeded.BrmComponent.Name}", capabilityNode.Label);
    }

    [Fact]
    public async Task BrmModelDetailsDependencyHierarchyIncludesCapabilitiesWithoutApplicationsAsync()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var seeded = await fixture.SeedHermAlignmentAsync();

        var secondArmDomain = new ArmDomain { Code = "AD002", Name = "Enrolment" };
        var secondArmCapability = new ArmCapability
        {
            Code = "AP002",
            Name = "Enrolment Services",
            ParentDomain = secondArmDomain,
            ParentDomainCode = secondArmDomain.Code
        };
        var secondArmComponent = new ArmComponent
        {
            Code = "AC002",
            Name = "Enrolment Portal",
            ParentCapability = secondArmCapability,
            ParentCapabilityCode = secondArmCapability.Code
        };

        var secondBrmDomain = new BrmDomain { Code = "BD002", Name = "Enrolment" };
        var secondBrmCapability = new BrmCapability
        {
            Code = "BC010",
            Name = "Enrolment Management",
            ParentDomain = secondBrmDomain,
            ParentDomainCode = secondBrmDomain.Code
        };
        var secondBrmComponent = new BrmComponent
        {
            Code = "BC011",
            Name = "Enrolment Services",
            ParentCapability = secondBrmCapability,
            ParentCapabilityCode = secondBrmCapability.Code
        };

        const string connectedCapabilityName = "BC002 Student Recruitment";
        const string disconnectedCapabilityName = "BC011 Enrolment Services";

        await fixture.DbContext.AddRangeAsync(
            secondArmDomain,
            secondArmCapability,
            secondArmComponent,
            new ArmComponentCapabilityLink
            {
                ArmComponent = secondArmComponent,
                ArmCapability = secondArmCapability
            },
            secondBrmDomain,
            secondBrmCapability,
            secondBrmComponent,
            new ApplicationCatalogItem
            {
                Name = "Admissions Hub",
                Mappings =
                [
                    new ApplicationCatalogItemMapping
                    {
                        ArmComponentId = seeded.ArmComponent.Id,
                        ProductMappingId = seeded.ProductMapping.Id,
                        ProductCatalogItemId = seeded.Product.Id
                    }
                ]
            },
            new BusinessCapabilityCatalogItem
            {
                BrmModelId = seeded.BrmModel.Id,
                Name = connectedCapabilityName,
                Mappings =
                [
                    new BusinessCapabilityCatalogItemMapping
                    {
                        BrmComponentId = seeded.BrmComponent.Id,
                        ArmComponentId = seeded.ArmComponent.Id,
                        ArmCapabilityId = seeded.ArmCapability.Id
                    }
                ]
            },
            new BusinessCapabilityCatalogItem
            {
                BrmModelId = seeded.BrmModel.Id,
                Name = disconnectedCapabilityName,
                Mappings =
                [
                    new BusinessCapabilityCatalogItemMapping
                    {
                        BrmComponent = secondBrmComponent,
                        ArmComponent = secondArmComponent,
                        ArmCapability = secondArmCapability
                    }
                ]
            });
        await fixture.DbContext.SaveChangesAsync();

        using var controller = fixture.CreateBrmModelsController();
        var result = await controller.DetailsAsync(seeded.BrmModel.Id);

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<BrmModelDetailsViewModel>(view.Model);
        Assert.True(model.HasDependencyTree);
        Assert.Equal(seeded.BrmModel.Name, model.HierarchyRoot.Label);
        Assert.Equal(2, model.HierarchyRoot.Children.Count);
        Assert.Contains(model.HierarchyRoot.Children, x => x.Label == connectedCapabilityName);

        var disconnectedNode = Assert.Single(model.HierarchyRoot.Children, x => x.Label == disconnectedCapabilityName);
        var brmDomainNode = Assert.Single(disconnectedNode.Children);
        var brmCapabilityNode = Assert.Single(brmDomainNode.Children);
        var brmComponentNode = Assert.Single(brmCapabilityNode.Children);
        var armDomainNode = Assert.Single(brmComponentNode.Children);
        var armCapabilityNode = Assert.Single(armDomainNode.Children);
        var armComponentNode = Assert.Single(armCapabilityNode.Children);

        Assert.Equal("AC002 Enrolment Portal", armComponentNode.Label);
        Assert.Empty(armComponentNode.Children);
    }

    [Fact]
    public async Task BrmModelCreateRejectsStatusOutsideDropdownOptionsAsync()
    {
        await using var fixture = await TestFixture.CreateAsync();

        using var controller = fixture.CreateBrmModelsController();
        var result = await controller.CreateAsync(new BrmModelEditViewModel
        {
            Name = "Operations BRM",
            Area = "Operations",
            Status = "Custom"
        });

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<BrmModelEditViewModel>(view.Model);
        Assert.False(controller.ModelState.IsValid);
        Assert.Contains(nameof(BrmModelEditViewModel.Status), controller.ModelState.Keys);
        Assert.Equal("Custom", model.Status);
    }

    [Fact]
    public async Task BrmModelDeleteConfirmedSoftDeletesModelAndWritesAuditAsync()
    {
        await using var fixture = await TestFixture.CreateAsync();

        fixture.DbContext.BrmModels.Add(new BrmModel
        {
            Name = "Student BRM",
            Area = "Student Services",
            Status = "Production"
        });
        await fixture.DbContext.SaveChangesAsync();

        var brmModelId = await fixture.DbContext.BrmModels.Select(x => x.Id).SingleAsync();

        using var controller = fixture.CreateBrmModelsController();
        var result = await controller.DeleteConfirmedAsync(brmModelId);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Index", redirect.ActionName);

        var brmModel = await fixture.DbContext.BrmModels.SingleAsync();
        Assert.True(brmModel.IsDeleted);
        Assert.NotNull(brmModel.DeletedUtc);
        Assert.Equal("Moved to trash from the BRM model catalogue.", brmModel.DeletedReason);
        Assert.Equal("Delete", (await fixture.DbContext.AuditLogEntries.SingleAsync()).Action);
    }

    [Fact]
    public async Task BrmModelRestoreDeletedClearsSoftDeleteStateAndWritesAuditAsync()
    {
        await using var fixture = await TestFixture.CreateAsync();

        fixture.DbContext.BrmModels.Add(new BrmModel
        {
            Name = "Student BRM",
            Area = "Student Services",
            Status = "Production",
            IsDeleted = true,
            DeletedUtc = DateTime.UtcNow.AddDays(-1),
            DeletedReason = "Moved to trash from the BRM model catalogue."
        });
        await fixture.DbContext.SaveChangesAsync();

        var brmModelId = await fixture.DbContext.BrmModels.Select(x => x.Id).SingleAsync();

        using var controller = fixture.CreateBrmModelsController();
        var result = await controller.RestoreDeletedAsync(brmModelId);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Restore", redirect.ActionName);

        var brmModel = await fixture.DbContext.BrmModels.SingleAsync();
        Assert.False(brmModel.IsDeleted);
        Assert.Null(brmModel.DeletedUtc);
        Assert.Null(brmModel.DeletedReason);
        Assert.Equal("Restore", (await fixture.DbContext.AuditLogEntries.SingleAsync()).Action);
    }

    [Fact]
    public async Task BrmModelIndexExcludesDeletedModelsAsync()
    {
        await using var fixture = await TestFixture.CreateAsync();

        await fixture.DbContext.AddRangeAsync(
            new BrmModel
            {
                Name = "Active BRM",
                Area = "Student Services",
                Status = "Production"
            },
            new BrmModel
            {
                Name = "Deleted BRM",
                Area = "Operations",
                Status = "Draft",
                IsDeleted = true,
                DeletedUtc = DateTime.UtcNow,
                DeletedReason = "Moved to trash from the BRM model catalogue."
            });
        await fixture.DbContext.SaveChangesAsync();

        using var controller = fixture.CreateBrmModelsController();
        var result = await controller.IndexAsync();

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<BrmModelsIndexViewModel>(view.Model);
        var row = Assert.Single(model.Models);
        Assert.Equal("Active BRM", row.Name);
    }

    [Fact]
    public async Task BrmModelDetailsReturnsNotFoundWhenModelDeletedAsync()
    {
        await using var fixture = await TestFixture.CreateAsync();

        fixture.DbContext.BrmModels.Add(new BrmModel
        {
            Name = "Deleted BRM",
            Area = "Operations",
            Status = "Draft",
            IsDeleted = true,
            DeletedUtc = DateTime.UtcNow,
            DeletedReason = "Moved to trash from the BRM model catalogue."
        });
        await fixture.DbContext.SaveChangesAsync();

        var brmModelId = await fixture.DbContext.BrmModels.Select(x => x.Id).SingleAsync();

        using var controller = fixture.CreateBrmModelsController();
        var result = await controller.DetailsAsync(brmModelId);

        Assert.IsType<NotFoundResult>(result);
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

        public BrmModelsController CreateBrmModelsController()
        {
            var controller = new BrmModelsController(
                DbContext,
                new AuditLogService(DbContext),
                new HermDrilldownService(DbContext));

            controller.TempData = new TempDataDictionary(new DefaultHttpContext(), new TestTempDataProvider());
            return controller;
        }

        public async Task<SeededHermAlignment> SeedHermAlignmentAsync()
        {
            var brmModel = new BrmModel
            {
                Name = "Student BRM",
                Area = "Student Services",
                Status = "Production"
            };
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
                brmModel,
                armDomain,
                armCapability,
                armComponent,
                new ArmComponentCapabilityLink
                {
                    ArmComponent = armComponent,
                    ArmCapability = armCapability
                },
                brmDomain,
                brmCapability,
                brmComponent,
                trmDomain,
                trmCapability,
                trmComponent,
                product);
            await DbContext.SaveChangesAsync();

            return new SeededHermAlignment(brmModel, armCapability, armComponent, brmComponent, product, product.Mappings.Single());
        }

        public async ValueTask DisposeAsync()
        {
            await DbContext.DisposeAsync();
            await connection.DisposeAsync();
        }
    }

    private sealed record SeededHermAlignment(
        BrmModel BrmModel,
        ArmCapability ArmCapability,
        ArmComponent ArmComponent,
        BrmComponent BrmComponent,
        ProductCatalogItem Product,
        ProductMapping ProductMapping);

    private sealed class TestTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();

        public void SaveTempData(HttpContext context, IDictionary<string, object> values)
        {
        }
    }
}
