using HERMMapperApp.Controllers;
using HERMMapperApp.Data;
using HERMMapperApp.Models;
using HERMMapperApp.Services;
using HERMMapperApp.ViewModels;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Reflection;
using Xunit;

namespace HERMMapperApp.Tests.Controllers;

public sealed class MappingsControllerTests
{
    private static readonly string[] DuplicateOwnerSelections = [" Team Blue ", "team blue", "", "Team Green"];

    [Fact]
    public async Task IndexFiltersProductsBySearchStatusDomainAndCapabilityAsync()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var domainA = new TrmDomain { Code = "TD001", Name = "Technology" };
        var domainB = new TrmDomain { Code = "TD002", Name = "Security" };
        var capabilityA = new TrmCapability { Code = "TP001", Name = "Observability", ParentDomain = domainA, ParentDomainCode = domainA.Code };
        var capabilityB = new TrmCapability { Code = "TP002", Name = "Identity", ParentDomain = domainB, ParentDomainCode = domainB.Code };
        var componentA = new TrmComponent { Code = "TC001", Name = "Monitoring", ParentCapability = capabilityA, ParentCapabilityCode = capabilityA.Code };
        var componentB = new TrmComponent { Code = "TC002", Name = "Identity", ParentCapability = capabilityB, ParentCapabilityCode = capabilityB.Code };
        var matchingProduct = new ProductCatalogItem { Name = "Sentinel Hub", Vendor = "Contoso" };
        var filteredOutProduct = new ProductCatalogItem { Name = "Identity Tool", Vendor = "Fabrikam" };
        await fixture.DbContext.AddRangeAsync(domainA, domainB, capabilityA, capabilityB, componentA, componentB, matchingProduct, filteredOutProduct);
        await fixture.DbContext.SaveChangesAsync();

        await fixture.DbContext.ProductMappings.AddRangeAsync(
            new ProductMapping
            {
                ProductCatalogItemId = matchingProduct.Id,
                TrmDomainId = domainA.Id,
                TrmCapabilityId = capabilityA.Id,
                TrmComponentId = componentA.Id,
                MappingStatus = MappingStatus.InReview
            },
            new ProductMapping
            {
                ProductCatalogItemId = filteredOutProduct.Id,
                TrmDomainId = domainB.Id,
                TrmCapabilityId = capabilityB.Id,
                TrmComponentId = componentB.Id,
                MappingStatus = MappingStatus.Complete
            });
        await fixture.DbContext.SaveChangesAsync();

        using var controller = fixture.CreateController();
        var result = await controller.IndexAsync("Sentinel", MappingStatus.InReview, domainA.Id, capabilityA.Id);

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<MappingBoardViewModel>(view.Model);
        Assert.Equal("Sentinel", model.Search);
        Assert.Equal(MappingStatus.InReview, model.Status);
        Assert.Equal(domainA.Id, model.DomainId);
        Assert.Equal(capabilityA.Id, model.CapabilityId);
        Assert.Single(model.Products);
        Assert.Equal(matchingProduct.Id, model.Products[0].Id);
    }

    [Fact]
    public async Task CreateGetReturnsEditViewForExistingProductAsync()
    {
        await using var fixture = await TestFixture.CreateAsync();
        await fixture.SeedOwnerOptionsAsync();
        var product = new ProductCatalogItem { Name = "Sentinel" };
        fixture.DbContext.ProductCatalogItems.Add(product);
        await fixture.DbContext.SaveChangesAsync();

        using var controller = fixture.CreateController();
        var result = await controller.CreateAsync(product.Id);

        var view = Assert.IsType<ViewResult>(result);
        Assert.Equal("Edit", view.ViewName);
        var model = Assert.IsType<MappingEditViewModel>(view.Model);
        Assert.Equal(product.Id, model.ProductId);
        Assert.Equal(product.Name, model.ProductName);
        Assert.False(model.AllowOwnerEditing.GetValueOrDefault());
    }

    [Fact]
    public async Task CreateGetReturnsNotFoundWhenProductMissingAsync()
    {
        await using var fixture = await TestFixture.CreateAsync();

        using var controller = fixture.CreateController();
        var result = await controller.CreateAsync(999);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task CreatePostReturnsNotFoundWhenProductMissingAsync()
    {
        await using var fixture = await TestFixture.CreateAsync();

        using var controller = fixture.CreateController();
        var result = await controller.CreateAsync(new MappingEditViewModel
        {
            ProductId = 999,
            MappingStatus = MappingStatus.Draft
        });

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task CreatePostRejectsSelectingExistingAndCustomComponentTogetherAsync()
    {
        await using var fixture = await TestFixture.CreateAsync();
        await fixture.SeedOwnerOptionsAsync();
        var domain = new TrmDomain { Code = "TD001", Name = "Technology" };
        var capability = new TrmCapability { Code = "TP001", Name = "Observability", ParentDomain = domain, ParentDomainCode = domain.Code };
        var component = new TrmComponent { Code = "TC001", Name = "Monitoring", ParentCapability = capability, ParentCapabilityCode = capability.Code };
        var product = new ProductCatalogItem { Name = "Sentinel" };
        await fixture.DbContext.AddRangeAsync(domain, capability, component, product);
        await fixture.DbContext.SaveChangesAsync();

        using var controller = fixture.CreateController();
        var result = await controller.CreateAsync(new MappingEditViewModel
        {
            ProductId = product.Id,
            SelectedDomainId = domain.Id,
            SelectedCapabilityId = capability.Id,
            SelectedComponentId = component.Id,
            CustomTechnologyComponentCode = "TECH-42",
            CustomComponentName = "Custom Hub",
            MappingStatus = MappingStatus.Draft
        });

        var view = Assert.IsType<ViewResult>(result);
        Assert.Equal("Edit", view.ViewName);
        Assert.Contains(controller.ModelState[nameof(MappingEditViewModel.SelectedComponentId)]!.Errors, error => error.ErrorMessage.Contains("not both", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task CreatePostRejectsIncompleteCustomComponentInputAsync()
    {
        await using var fixture = await TestFixture.CreateAsync();
        await fixture.SeedOwnerOptionsAsync();
        var product = new ProductCatalogItem { Name = "Sentinel" };
        fixture.DbContext.ProductCatalogItems.Add(product);
        await fixture.DbContext.SaveChangesAsync();

        using var controller = fixture.CreateController();
        var result = await controller.CreateAsync(new MappingEditViewModel
        {
            ProductId = product.Id,
            CustomTechnologyComponentCode = "TECH-42",
            MappingStatus = MappingStatus.Draft
        });

        Assert.IsType<ViewResult>(result);
        Assert.Contains(controller.ModelState[nameof(MappingEditViewModel.CustomTechnologyComponentCode)]!.Errors, error => error.ErrorMessage.Contains("needs both", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task CreatePostRejectsCustomComponentWhenCapabilityIsMissingAsync()
    {
        await using var fixture = await TestFixture.CreateAsync();
        await fixture.SeedOwnerOptionsAsync();
        var product = new ProductCatalogItem { Name = "Sentinel" };
        fixture.DbContext.ProductCatalogItems.Add(product);
        await fixture.DbContext.SaveChangesAsync();

        using var controller = fixture.CreateController();
        var result = await controller.CreateAsync(new MappingEditViewModel
        {
            ProductId = product.Id,
            CustomTechnologyComponentCode = "TECH-42",
            CustomComponentName = "Custom Hub",
            MappingStatus = MappingStatus.Draft
        });

        Assert.IsType<ViewResult>(result);
        Assert.Contains(controller.ModelState[nameof(MappingEditViewModel.SelectedCapabilityId)]!.Errors, error => error.ErrorMessage.Contains("Choose a TRM capability", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task CreatePostRejectsUnknownCapabilityForCustomComponentAsync()
    {
        await using var fixture = await TestFixture.CreateAsync();
        await fixture.SeedOwnerOptionsAsync();
        var product = new ProductCatalogItem { Name = "Sentinel" };
        fixture.DbContext.ProductCatalogItems.Add(product);
        await fixture.DbContext.SaveChangesAsync();

        using var controller = fixture.CreateController();
        var result = await controller.CreateAsync(new MappingEditViewModel
        {
            ProductId = product.Id,
            SelectedCapabilityId = 999,
            CustomTechnologyComponentCode = "TECH-42",
            CustomComponentName = "Custom Hub",
            MappingStatus = MappingStatus.Draft
        });

        Assert.IsType<ViewResult>(result);
        Assert.Contains(controller.ModelState[nameof(MappingEditViewModel.SelectedCapabilityId)]!.Errors, error => error.ErrorMessage.Contains("valid HERM TRM capability", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task CreatePostRejectsCompletedMappingWithoutComponentAsync()
    {
        await using var fixture = await TestFixture.CreateAsync();
        await fixture.SeedOwnerOptionsAsync();
        var domain = new TrmDomain { Code = "TD001", Name = "Technology" };
        var product = new ProductCatalogItem { Name = "Sentinel" };
        await fixture.DbContext.AddRangeAsync(domain, product);
        await fixture.DbContext.SaveChangesAsync();

        using var controller = fixture.CreateController();
        var result = await controller.CreateAsync(new MappingEditViewModel
        {
            ProductId = product.Id,
            SelectedDomainId = domain.Id,
            MappingStatus = MappingStatus.Complete
        });

        Assert.IsType<ViewResult>(result);
        Assert.Contains(controller.ModelState[nameof(MappingEditViewModel.SelectedComponentId)]!.Errors, error => error.ErrorMessage.Contains("must select", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task CreatePostRejectsComponentThatDoesNotBelongToSelectedCapabilityAsync()
    {
        await using var fixture = await TestFixture.CreateAsync();
        await fixture.SeedOwnerOptionsAsync();
        var domain = new TrmDomain { Code = "TD001", Name = "Technology" };
        var capabilityA = new TrmCapability { Code = "TP001", Name = "Observability", ParentDomain = domain, ParentDomainCode = domain.Code };
        var capabilityB = new TrmCapability { Code = "TP002", Name = "Identity", ParentDomain = domain, ParentDomainCode = domain.Code };
        var component = new TrmComponent { Code = "TC001", Name = "Monitoring", ParentCapability = capabilityA, ParentCapabilityCode = capabilityA.Code };
        var product = new ProductCatalogItem { Name = "Sentinel" };
        await fixture.DbContext.AddRangeAsync(domain, capabilityA, capabilityB, component, product);
        await fixture.DbContext.SaveChangesAsync();

        using var controller = fixture.CreateController();
        var result = await controller.CreateAsync(new MappingEditViewModel
        {
            ProductId = product.Id,
            SelectedDomainId = domain.Id,
            SelectedCapabilityId = capabilityB.Id,
            SelectedComponentId = component.Id,
            MappingStatus = MappingStatus.Draft
        });

        Assert.IsType<ViewResult>(result);
        Assert.Contains(controller.ModelState[nameof(MappingEditViewModel.SelectedCapabilityId)]!.Errors, error => error.ErrorMessage.Contains("does not belong", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task CreatePostRejectsCapabilityThatDoesNotBelongToSelectedDomainAsync()
    {
        await using var fixture = await TestFixture.CreateAsync();
        await fixture.SeedOwnerOptionsAsync();
        var domainA = new TrmDomain { Code = "TD001", Name = "Technology" };
        var domainB = new TrmDomain { Code = "TD002", Name = "Security" };
        var capability = new TrmCapability { Code = "TP001", Name = "Observability", ParentDomain = domainA, ParentDomainCode = domainA.Code };
        var product = new ProductCatalogItem { Name = "Sentinel" };
        await fixture.DbContext.AddRangeAsync(domainA, domainB, capability, product);
        await fixture.DbContext.SaveChangesAsync();

        using var controller = fixture.CreateController();
        var result = await controller.CreateAsync(new MappingEditViewModel
        {
            ProductId = product.Id,
            SelectedDomainId = domainB.Id,
            SelectedCapabilityId = capability.Id,
            MappingStatus = MappingStatus.Draft
        });

        Assert.IsType<ViewResult>(result);
        Assert.Contains(controller.ModelState[nameof(MappingEditViewModel.SelectedDomainId)]!.Errors, error => error.ErrorMessage.Contains("does not belong", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task CreatePostRejectsCustomTechnologyCodeWhenModelComponentAlreadyExistsAsync()
    {
        await using var fixture = await TestFixture.CreateAsync();
        await fixture.SeedOwnerOptionsAsync();
        var domain = new TrmDomain { Code = "TD001", Name = "Technology" };
        var capability = new TrmCapability { Code = "TP001", Name = "Observability", ParentDomain = domain, ParentDomainCode = domain.Code };
        var modelComponent = new TrmComponent { Code = "TC042", Name = "Monitoring", ParentCapability = capability, ParentCapabilityCode = capability.Code };
        var product = new ProductCatalogItem { Name = "Sentinel" };
        await fixture.DbContext.AddRangeAsync(domain, capability, modelComponent, product);
        await fixture.DbContext.SaveChangesAsync();

        using var controller = fixture.CreateController();
        var result = await controller.CreateAsync(new MappingEditViewModel
        {
            ProductId = product.Id,
            SelectedCapabilityId = capability.Id,
            CustomTechnologyComponentCode = "TC042",
            CustomComponentName = "Custom Monitoring",
            MappingStatus = MappingStatus.Draft
        });

        Assert.IsType<ViewResult>(result);
        Assert.Contains(controller.ModelState[nameof(MappingEditViewModel.CustomTechnologyComponentCode)]!.Errors, error => error.ErrorMessage.Contains("already exists", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task CreatePostReusesExistingCustomComponentAndAddsMissingCapabilityLinkAsync()
    {
        await using var fixture = await TestFixture.CreateAsync();
        await fixture.SeedOwnerOptionsAsync();
        var domain = new TrmDomain { Code = "TD001", Name = "Technology" };
        var capabilityA = new TrmCapability { Code = "TP001", Name = "Observability", ParentDomain = domain, ParentDomainCode = domain.Code };
        var capabilityB = new TrmCapability { Code = "TP002", Name = "Identity", ParentDomain = domain, ParentDomainCode = domain.Code };
        var customComponent = new TrmComponent
        {
            Code = "CUS00000001",
            TechnologyComponentCode = "TECH-42",
            Name = "Old Name",
            IsCustom = true,
            ParentCapabilityId = null,
            ParentCapabilityCode = string.Empty
        };
        var product = new ProductCatalogItem { Name = "Sentinel" };
        await fixture.DbContext.AddRangeAsync(domain, capabilityA, capabilityB, customComponent, product);
        await fixture.DbContext.SaveChangesAsync();

        using var controller = fixture.CreateController();
        var result = await controller.CreateAsync(new MappingEditViewModel
        {
            ProductId = product.Id,
            Owners = ["Team Blue"],
            SelectedDomainId = domain.Id,
            SelectedCapabilityId = capabilityB.Id,
            CustomTechnologyComponentCode = "TECH-42",
            CustomComponentName = "Updated Name",
            MappingStatus = MappingStatus.Draft
        });

        Assert.IsType<RedirectToActionResult>(result);
        var component = await fixture.DbContext.TrmComponents.Include(x => x.CapabilityLinks).SingleAsync();
        Assert.Equal("Updated Name", component.Name);
        Assert.Equal(capabilityB.Id, component.ParentCapabilityId);
        Assert.Contains(component.CapabilityLinks, link => link.TrmCapabilityId == capabilityB.Id);
    }

    [Fact]
    public async Task CreatePostRejectsUnknownSelectedCapabilityAsync()
    {
        await using var fixture = await TestFixture.CreateAsync();
        await fixture.SeedOwnerOptionsAsync();
        var product = new ProductCatalogItem { Name = "Sentinel" };
        fixture.DbContext.ProductCatalogItems.Add(product);
        await fixture.DbContext.SaveChangesAsync();

        using var controller = fixture.CreateController();
        var result = await controller.CreateAsync(new MappingEditViewModel
        {
            ProductId = product.Id,
            SelectedCapabilityId = 999,
            MappingStatus = MappingStatus.Draft
        });

        Assert.IsType<ViewResult>(result);
        Assert.Contains(controller.ModelState[nameof(MappingEditViewModel.SelectedCapabilityId)]!.Errors, error => error.ErrorMessage.Contains("valid HERM TRM capability", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task CreatePostRejectsUnknownSelectedDomainAsync()
    {
        await using var fixture = await TestFixture.CreateAsync();
        await fixture.SeedOwnerOptionsAsync();
        var product = new ProductCatalogItem { Name = "Sentinel" };
        fixture.DbContext.ProductCatalogItems.Add(product);
        await fixture.DbContext.SaveChangesAsync();

        using var controller = fixture.CreateController();
        var result = await controller.CreateAsync(new MappingEditViewModel
        {
            ProductId = product.Id,
            SelectedDomainId = 999,
            MappingStatus = MappingStatus.Draft
        });

        Assert.IsType<ViewResult>(result);
        Assert.Contains(controller.ModelState[nameof(MappingEditViewModel.SelectedDomainId)]!.Errors, error => error.ErrorMessage.Contains("valid HERM TRM domain", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task CreatePostRejectsUnknownSelectedComponentAsync()
    {
        await using var fixture = await TestFixture.CreateAsync();
        await fixture.SeedOwnerOptionsAsync();
        var domain = new TrmDomain { Code = "TD001", Name = "Technology" };
        var capability = new TrmCapability { Code = "TP001", Name = "Observability", ParentDomain = domain, ParentDomainCode = domain.Code };
        var product = new ProductCatalogItem { Name = "Sentinel" };
        await fixture.DbContext.AddRangeAsync(domain, capability, product);
        await fixture.DbContext.SaveChangesAsync();

        using var controller = fixture.CreateController();
        var result = await controller.CreateAsync(new MappingEditViewModel
        {
            ProductId = product.Id,
            SelectedDomainId = domain.Id,
            SelectedCapabilityId = capability.Id,
            SelectedComponentId = 999,
            MappingStatus = MappingStatus.Draft
        });

        Assert.IsType<ViewResult>(result);
        Assert.Contains(controller.ModelState[nameof(MappingEditViewModel.SelectedComponentId)]!.Errors, error => error.ErrorMessage.Contains("valid HERM TRM component", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task CreatePostRejectsComponentWithoutCompleteHierarchyAsync()
    {
        await using var fixture = await TestFixture.CreateAsync();
        await fixture.SeedOwnerOptionsAsync();
        var product = new ProductCatalogItem { Name = "Sentinel" };
        var component = new TrmComponent { Code = "TC001", Name = "Orphaned Component" };
        await fixture.DbContext.AddRangeAsync(product, component);
        await fixture.DbContext.SaveChangesAsync();

        using var controller = fixture.CreateController();
        var result = await controller.CreateAsync(new MappingEditViewModel
        {
            ProductId = product.Id,
            SelectedComponentId = component.Id,
            MappingStatus = MappingStatus.Draft
        });

        Assert.IsType<ViewResult>(result);
        Assert.Contains(controller.ModelState[nameof(MappingEditViewModel.SelectedComponentId)]!.Errors, error => error.ErrorMessage.Contains("complete parent capability/domain hierarchy", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task EditPostSynchronizesOwnersWhenSelectionChangesAsync()
    {
        await using var fixture = await TestFixture.CreateAsync();
        await fixture.SeedOwnerOptionsAsync();
        var domain = new TrmDomain { Code = "TD001", Name = "Technology" };
        var capability = new TrmCapability { Code = "TP001", Name = "Observability", ParentDomain = domain, ParentDomainCode = domain.Code };
        var component = new TrmComponent { Code = "TC001", Name = "Monitoring", ParentCapability = capability, ParentCapabilityCode = capability.Code };
        var product = new ProductCatalogItem { Name = "Sentinel" };
        product.Owners.Add(new ProductCatalogItemOwner { OwnerValue = "Team Blue" });
        var mapping = new ProductMapping { ProductCatalogItem = product, TrmDomain = domain, TrmCapability = capability, TrmComponent = component, MappingStatus = MappingStatus.Draft };
        await fixture.DbContext.AddRangeAsync(domain, capability, component, product, mapping);
        await fixture.DbContext.SaveChangesAsync();

        using var controller = fixture.CreateController();
        var result = await controller.EditAsync(mapping.Id, new MappingEditViewModel
        {
            MappingId = mapping.Id,
            ProductId = product.Id,
            Owners = ["Team Green"],
            SelectedDomainId = domain.Id,
            SelectedCapabilityId = capability.Id,
            SelectedComponentId = component.Id,
            MappingStatus = MappingStatus.Complete
        });

        Assert.IsType<RedirectToActionResult>(result);
        var owners = await fixture.DbContext.ProductCatalogItemOwners.OrderBy(x => x.OwnerValue).Select(x => x.OwnerValue).ToListAsync();
        Assert.Equal(["Team Green"], owners);
    }

    [Fact]
    public async Task CreatePostDoesNotSynchronizeOwnersWhenSelectionChangesAsync()
    {
        await using var fixture = await TestFixture.CreateAsync();
        await fixture.SeedOwnerOptionsAsync();
        var domain = new TrmDomain { Code = "TD001", Name = "Technology" };
        var capability = new TrmCapability { Code = "TP001", Name = "Observability", ParentDomain = domain, ParentDomainCode = domain.Code };
        var component = new TrmComponent { Code = "TC001", Name = "Monitoring", ParentCapability = capability, ParentCapabilityCode = capability.Code };
        var product = new ProductCatalogItem { Name = "Sentinel" };
        product.Owners.Add(new ProductCatalogItemOwner { OwnerValue = "Team Blue" });
        await fixture.DbContext.AddRangeAsync(domain, capability, component, product);
        await fixture.DbContext.SaveChangesAsync();

        using var controller = fixture.CreateController();
        var result = await controller.CreateAsync(new MappingEditViewModel
        {
            ProductId = product.Id,
            Owners = ["Team Green"],
            SelectedDomainId = domain.Id,
            SelectedCapabilityId = capability.Id,
            SelectedComponentId = component.Id,
            MappingStatus = MappingStatus.Complete
        });

        Assert.IsType<RedirectToActionResult>(result);
        var owners = await fixture.DbContext.ProductCatalogItemOwners.OrderBy(x => x.OwnerValue).Select(x => x.OwnerValue).ToListAsync();
        Assert.Equal(["Team Blue"], owners);
    }

    [Fact]
    public async Task EditGetReturnsExistingMappingAsync()
    {
        await using var fixture = await TestFixture.CreateAsync();
        await fixture.SeedOwnerOptionsAsync();
        var domain = new TrmDomain { Code = "TD001", Name = "Technology" };
        var capability = new TrmCapability { Code = "TP001", Name = "Observability", ParentDomain = domain, ParentDomainCode = domain.Code };
        var component = new TrmComponent { Code = "TC001", Name = "Monitoring", ParentCapability = capability, ParentCapabilityCode = capability.Code };
        var product = new ProductCatalogItem { Name = "Sentinel" };
        var mapping = new ProductMapping { ProductCatalogItem = product, TrmDomain = domain, TrmCapability = capability, TrmComponent = component, MappingStatus = MappingStatus.Complete };
        await fixture.DbContext.AddRangeAsync(domain, capability, component, product, mapping);
        await fixture.DbContext.SaveChangesAsync();

        using var controller = fixture.CreateController();
        var result = await controller.EditAsync(mapping.Id);

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<MappingEditViewModel>(view.Model);
        Assert.Equal(mapping.Id, model.MappingId);
        Assert.Equal(component.Id, model.SelectedComponentId);
        Assert.True(model.AllowOwnerEditing.GetValueOrDefault());
    }

    [Fact]
    public async Task EditGetReturnsNotFoundWhenMappingMissingAsync()
    {
        await using var fixture = await TestFixture.CreateAsync();

        using var controller = fixture.CreateController();
        var result = await controller.EditAsync(999);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task EditPostUpdatesExistingCustomComponentNameAsync()
    {
        await using var fixture = await TestFixture.CreateAsync();
        await fixture.SeedOwnerOptionsAsync();
        var domain = new TrmDomain { Code = "TD001", Name = "Technology" };
        var capability = new TrmCapability { Code = "TP001", Name = "Observability", ParentDomain = domain, ParentDomainCode = domain.Code };
        var customComponent = new TrmComponent
        {
            Code = "CUS00000001",
            TechnologyComponentCode = "TECH-42",
            Name = "Old Name",
            ParentCapability = capability,
            ParentCapabilityCode = capability.Code,
            IsCustom = true
        };
        customComponent.CapabilityLinks.Add(new TrmComponentCapabilityLink { TrmCapability = capability });
        var product = new ProductCatalogItem { Name = "Sentinel" };
        var mapping = new ProductMapping { ProductCatalogItem = product, TrmDomain = domain, TrmCapability = capability, TrmComponent = customComponent, MappingStatus = MappingStatus.Draft };
        await fixture.DbContext.AddRangeAsync(domain, capability, customComponent, product, mapping);
        await fixture.DbContext.SaveChangesAsync();

        using var controller = fixture.CreateController();
        var result = await controller.EditAsync(mapping.Id, new MappingEditViewModel
        {
            MappingId = mapping.Id,
            ProductId = product.Id,
            Owners = ["Team Blue"],
            SelectedDomainId = domain.Id,
            SelectedCapabilityId = capability.Id,
            CustomTechnologyComponentCode = "TECH-42",
            CustomComponentName = "New Name",
            MappingStatus = MappingStatus.Complete,
            MappingRationale = "Updated"
        });

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Index", redirect.ActionName);
        var component = await fixture.DbContext.TrmComponents.SingleAsync();
        Assert.Equal("New Name", component.Name);
        Assert.Equal("Updated", (await fixture.DbContext.TrmComponentVersions.SingleAsync()).ChangeType);
    }

    [Fact]
    public async Task EditPostReturnsNotFoundWhenMappingMissingAsync()
    {
        await using var fixture = await TestFixture.CreateAsync();

        using var controller = fixture.CreateController();
        var result = await controller.EditAsync(999, new MappingEditViewModel { ProductId = 1, MappingStatus = MappingStatus.Draft });

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task EditPostReturnsViewWhenMappingValidationFailsAsync()
    {
        await using var fixture = await TestFixture.CreateAsync();
        await fixture.SeedOwnerOptionsAsync();
        var domain = new TrmDomain { Code = "TD001", Name = "Technology" };
        var capability = new TrmCapability { Code = "TP001", Name = "Observability", ParentDomain = domain, ParentDomainCode = domain.Code };
        var component = new TrmComponent { Code = "TC001", Name = "Monitoring", ParentCapability = capability, ParentCapabilityCode = capability.Code };
        var product = new ProductCatalogItem { Name = "Sentinel" };
        var mapping = new ProductMapping { ProductCatalogItem = product, TrmDomain = domain, TrmCapability = capability, TrmComponent = component, MappingStatus = MappingStatus.Draft };
        await fixture.DbContext.AddRangeAsync(domain, capability, component, product, mapping);
        await fixture.DbContext.SaveChangesAsync();

        using var controller = fixture.CreateController();
        var result = await controller.EditAsync(mapping.Id, new MappingEditViewModel
        {
            MappingId = mapping.Id,
            ProductId = product.Id,
            SelectedDomainId = domain.Id,
            SelectedCapabilityId = capability.Id,
            SelectedComponentId = 999,
            MappingStatus = MappingStatus.Draft
        });

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<MappingEditViewModel>(view.Model);
        Assert.Equal(mapping.Id, model.MappingId);
        Assert.Contains(controller.ModelState[nameof(MappingEditViewModel.SelectedComponentId)]!.Errors, error => error.ErrorMessage.Contains("valid HERM TRM component", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task DeleteReturnsNotFoundWhenMappingMissingAsync()
    {
        await using var fixture = await TestFixture.CreateAsync();

        using var controller = fixture.CreateController();
        var result = await controller.DeleteAsync(123);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task DeleteReturnsViewWhenMappingExistsAsync()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var product = new ProductCatalogItem { Name = "Sentinel" };
        var mapping = new ProductMapping { ProductCatalogItem = product, MappingStatus = MappingStatus.Draft };
        await fixture.DbContext.AddRangeAsync(product, mapping);
        await fixture.DbContext.SaveChangesAsync();

        using var controller = fixture.CreateController();
        var result = await controller.DeleteAsync(mapping.Id);

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<ProductMapping>(view.Model);
        Assert.Equal(mapping.Id, model.Id);
        Assert.Equal($"/Products/Details/{product.Id}", view.ViewData["ReturnUrl"]);
    }

    [Fact]
    public async Task DeleteConfirmedReturnsNotFoundWhenMappingMissingAsync()
    {
        await using var fixture = await TestFixture.CreateAsync();

        using var controller = fixture.CreateController();
        var result = await controller.DeleteConfirmedAsync(999);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task DeleteConfirmedRemovesMappingAndUpdatesProductTimestampAsync()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var product = new ProductCatalogItem { Name = "Sentinel", UpdatedUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc) };
        var mapping = new ProductMapping { ProductCatalogItem = product, MappingStatus = MappingStatus.Draft };
        await fixture.DbContext.AddRangeAsync(product, mapping);
        await fixture.DbContext.SaveChangesAsync();

        using var controller = fixture.CreateController();
        var result = await controller.DeleteConfirmedAsync(mapping.Id);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Details", redirect.ActionName);
        Assert.Equal("Products", redirect.ControllerName);
        Assert.Equal(product.Id, redirect.RouteValues!["id"]);
        Assert.Equal(0, await fixture.DbContext.ProductMappings.CountAsync());
        Assert.True((await fixture.DbContext.ProductCatalogItems.SingleAsync()).UpdatedUtc > new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        Assert.Equal("Delete", (await fixture.DbContext.AuditLogEntries.SingleAsync()).Action);
    }

    [Fact]
    public async Task DeleteConfirmedRemovesDependentApplicationMappingsAsync()
    {
        await using var fixture = await TestFixture.CreateAsync();

        var trmDomain = new TrmDomain { Code = "TD001", Name = "Technology" };
        var trmCapability = new TrmCapability { Code = "TP001", Name = "Observability", ParentDomain = trmDomain, ParentDomainCode = trmDomain.Code };
        var trmComponent = new TrmComponent { Code = "TC001", Name = "Monitoring", ParentCapability = trmCapability, ParentCapabilityCode = trmCapability.Code };
        var armDomain = new ArmDomain { Code = "AD001", Name = "Applications" };
        var armCapability = new ArmCapability { Code = "AP001", Name = "Recruitment", ParentDomain = armDomain, ParentDomainCode = armDomain.Code };
        var armComponent = new ArmComponent { Code = "AC001", Name = "Portal", ParentCapability = armCapability, ParentCapabilityCode = armCapability.Code };
        var product = new ProductCatalogItem { Name = "Sentinel", UpdatedUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc) };
        var application = new ApplicationCatalogItem { Name = "Admissions", CreatedUtc = DateTime.UtcNow, UpdatedUtc = DateTime.UtcNow };

        await fixture.DbContext.AddRangeAsync(trmDomain, trmCapability, trmComponent, armDomain, armCapability, armComponent, product, application);
        await fixture.DbContext.SaveChangesAsync();

        var mapping = new ProductMapping { ProductCatalogItemId = product.Id, TrmDomainId = trmDomain.Id, TrmCapabilityId = trmCapability.Id, TrmComponentId = trmComponent.Id, MappingStatus = MappingStatus.Complete };
        await fixture.DbContext.ProductMappings.AddAsync(mapping);
        await fixture.DbContext.SaveChangesAsync();

        await fixture.DbContext.ApplicationCatalogItemMappings.AddAsync(new ApplicationCatalogItemMapping
        {
            ApplicationCatalogItemId = application.Id,
            ArmComponentId = armComponent.Id,
            ProductMappingId = mapping.Id,
            ProductCatalogItemId = product.Id,
            CreatedUtc = DateTime.UtcNow
        });
        await fixture.DbContext.SaveChangesAsync();

        using var controller = fixture.CreateController();
        var result = await controller.DeleteConfirmedAsync(mapping.Id);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Details", redirect.ActionName);
        Assert.Equal("Products", redirect.ControllerName);
        Assert.Equal(product.Id, redirect.RouteValues!["id"]);
        Assert.Equal(0, await fixture.DbContext.ProductMappings.CountAsync());
        Assert.Equal(0, await fixture.DbContext.ApplicationCatalogItemMappings.CountAsync());
    }

    [Fact]
    public async Task DeleteConfirmedRedirectsToSuppliedReturnUrlWhenItIsLocalAsync()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var product = new ProductCatalogItem { Name = "Sentinel" };
        var mapping = new ProductMapping { ProductCatalogItem = product, MappingStatus = MappingStatus.Draft };
        await fixture.DbContext.AddRangeAsync(product, mapping);
        await fixture.DbContext.SaveChangesAsync();

        using var controller = fixture.CreateController();
        var result = await controller.DeleteConfirmedAsync(mapping.Id, "/Products/Details/42?tab=mappings");

        var redirect = Assert.IsType<LocalRedirectResult>(result);
        Assert.Equal("/Products/Details/42?tab=mappings", redirect.Url);
    }

    [Fact]
    public async Task ExportCsvIncludesUnfinishedMappingsWhenRequestedAndAppliesFiltersAsync()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var domain = new TrmDomain { Code = "TD001", Name = "Technology" };
        var capability = new TrmCapability { Code = "TP001", Name = "Observability", ParentDomain = domain, ParentDomainCode = domain.Code };
        var component = new TrmComponent { Code = "TC001", Name = "Monitoring", ParentCapability = capability, ParentCapabilityCode = capability.Code };
        var matchingProduct = new ProductCatalogItem { Name = "Sentinel" };
        var filteredProduct = new ProductCatalogItem { Name = "Atlas" };
        await fixture.DbContext.AddRangeAsync(domain, capability, component, matchingProduct, filteredProduct);
        await fixture.DbContext.SaveChangesAsync();

        await fixture.DbContext.ProductMappings.AddRangeAsync(
            new ProductMapping
            {
                ProductCatalogItemId = matchingProduct.Id,
                TrmDomainId = domain.Id,
                TrmCapabilityId = capability.Id,
                TrmComponentId = component.Id,
                MappingStatus = MappingStatus.Draft
            },
            new ProductMapping
            {
                ProductCatalogItemId = filteredProduct.Id,
                TrmDomainId = domain.Id,
                TrmCapabilityId = capability.Id,
                TrmComponentId = component.Id,
                MappingStatus = MappingStatus.Complete
            });
        await fixture.DbContext.SaveChangesAsync();

        using var controller = fixture.CreateController();
        var result = await controller.ExportCsvAsync("Sentinel", MappingStatus.Draft, domain.Id, capability.Id, includeUnfinished: true);

        var file = Assert.IsType<FileContentResult>(result);
        var content = System.Text.Encoding.UTF8.GetString(file.FileContents);
        Assert.Contains("Sentinel", content);
        Assert.DoesNotContain("Atlas", content);
    }
    [Fact]
    public async Task CapabilitiesReturnsFilteredCapabilitiesAsync()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var domainA = new TrmDomain { Code = "TD001", Name = "Technology" };
        var domainB = new TrmDomain { Code = "TD002", Name = "Security" };
        var capabilityA = new TrmCapability { Code = "TP001", Name = "Observability", ParentDomain = domainA, ParentDomainCode = domainA.Code };
        var capabilityB = new TrmCapability { Code = "TP002", Name = "Identity", ParentDomain = domainB, ParentDomainCode = domainB.Code };
        await fixture.DbContext.AddRangeAsync(domainA, domainB, capabilityA, capabilityB);
        await fixture.DbContext.SaveChangesAsync();

        using var controller = fixture.CreateController();
        var result = await controller.CapabilitiesAsync(domainA.Id);

        var json = Assert.IsType<JsonResult>(result);
        var items = ToDictionaryList(json.Value);
        Assert.Single(items);
        Assert.Equal(capabilityA.Id, items[0]["id"]);
        Assert.Equal("TP001 Observability", items[0]["text"]);
    }

    [Fact]
    public async Task ComponentsExcludesDeletedComponentsAndOrdersModelBeforeCustomAsync()
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
        await fixture.DbContext.AddRangeAsync(domain, capability, modelComponent, customComponent, deletedComponent);
        await fixture.DbContext.SaveChangesAsync();

        using var controller = fixture.CreateController();
        var result = await controller.ComponentsAsync(capability.Id);

        var json = Assert.IsType<JsonResult>(result);
        var items = ToDictionaryList(json.Value);
        Assert.Equal(2, items.Count);
        Assert.Equal("TC001 Monitoring", items[0]["text"]);
        Assert.Equal("TECH-1 Custom Monitoring", items[1]["text"]);
    }

    [Fact]
    public async Task ComponentsReturnsAllNonDeletedComponentsWhenCapabilityMissingAsync()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var domain = new TrmDomain { Code = "TD001", Name = "Technology" };
        var capabilityA = new TrmCapability { Code = "TP001", Name = "Observability", ParentDomain = domain, ParentDomainCode = domain.Code };
        var capabilityB = new TrmCapability { Code = "TP002", Name = "Identity", ParentDomain = domain, ParentDomainCode = domain.Code };
        var componentA = new TrmComponent { Code = "TC001", Name = "Monitoring", ParentCapability = capabilityA, ParentCapabilityCode = capabilityA.Code };
        var componentB = new TrmComponent { Code = "TC002", Name = "Gateway", ParentCapability = capabilityB, ParentCapabilityCode = capabilityB.Code };
        var deletedComponent = new TrmComponent { Code = "TC003", Name = "Deleted", ParentCapability = capabilityB, ParentCapabilityCode = capabilityB.Code, IsDeleted = true };
        await fixture.DbContext.AddRangeAsync(domain, capabilityA, capabilityB, componentA, componentB, deletedComponent);
        await fixture.DbContext.SaveChangesAsync();

        using var controller = fixture.CreateController();
        var result = await controller.ComponentsAsync(null);

        var json = Assert.IsType<JsonResult>(result);
        var items = ToDictionaryList(json.Value);
        Assert.Equal(2, items.Count);
        Assert.Contains(items, item => Equals(item["text"], "TC001 Monitoring"));
        Assert.Contains(items, item => Equals(item["text"], "TC002 Gateway"));
    }

    [Fact]
    public async Task CreatePostWithCustomComponentCreatesMappingComponentHistoryAndAuditAsync()
    {
        await using var fixture = await TestFixture.CreateAsync();
        await fixture.SeedOwnerOptionsAsync();

        var domain = new TrmDomain { Code = "TD001", Name = "Technology" };
        var capability = new TrmCapability { Code = "TP001", Name = "Observability", ParentDomain = domain, ParentDomainCode = domain.Code };
        var product = new ProductCatalogItem { Name = "Sentinel" };
        await fixture.DbContext.AddRangeAsync(domain, capability, product);
        await fixture.DbContext.SaveChangesAsync();

        using var controller = fixture.CreateController();
        var result = await controller.CreateAsync(new MappingEditViewModel
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
        Assert.Equal("Index", redirect.ActionName);

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
        Assert.Empty(persistedProduct.GetOwnerValues());
        Assert.Equal(2, audits.Count);
        Assert.Contains(audits, entry => entry.Category == "Mapping" && entry.Action == "Create");
        Assert.Contains(audits, entry => entry.Category == "Component" && entry.Action == "Create");
    }

    [Fact]
    public async Task ExportCsvReturnsOnlyCompletedMappingsWhenIncludeUnfinishedIsFalseAsync()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var domain = new TrmDomain { Code = "TD001", Name = "Technology" };
        var capability = new TrmCapability { Code = "TP001", Name = "Observability", ParentDomain = domain, ParentDomainCode = domain.Code };
        var component = new TrmComponent { Code = "TC001", Name = "Monitoring", ParentCapability = capability, ParentCapabilityCode = capability.Code };
        var productA = new ProductCatalogItem { Name = "Sentinel" };
        var productB = new ProductCatalogItem { Name = "Draft Tool" };
        await fixture.DbContext.AddRangeAsync(domain, capability, component, productA, productB);
        await fixture.DbContext.SaveChangesAsync();

        await fixture.DbContext.ProductMappings.AddRangeAsync(
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

        using var controller = fixture.CreateController();
        var result = await controller.ExportCsvAsync(null, null, null, null, includeUnfinished: false);

        var file = Assert.IsType<FileContentResult>(result);
        Assert.Equal("text/csv", file.ContentType);
        var content = System.Text.Encoding.UTF8.GetString(file.FileContents);
        Assert.Contains("Sentinel", content);
        Assert.DoesNotContain("Draft Tool", content);
    }

    [Fact]
    public async Task AddWithAiReturnsLoadingViewWhenLookupStartsAsync()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var domain = new TrmDomain { Code = "TD001", Name = "Technology" };
        var capability = new TrmCapability { Code = "TP001", Name = "Identity", ParentDomain = domain, ParentDomainCode = domain.Code };
        var component = new TrmComponent { Code = "TC001", Name = "Directory Service", ParentCapability = capability, ParentCapabilityCode = capability.Code };
        var product = new ProductCatalogItem { Name = "Active Directory" };
        await fixture.DbContext.AddRangeAsync(domain, capability, component, product);
        await fixture.SeedAiSettingsAsync();
        await fixture.DbContext.SaveChangesAsync();

        using var controller = fixture.CreateController();
        var result = await controller.AddWithAiAsync(product.Id);

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<AiMappingReviewViewModel>(view.Model);
        Assert.Equal(product.Id, model.ProductId);
        Assert.True(model.AutoStartLookup);
        Assert.False(model.LookupCompleted);
        Assert.Empty(model.Suggestions);
        Assert.Equal(AppSettingDefaults.AiMappingTimeoutSeconds, model.TimeoutSeconds);
    }

    [Fact]
    public async Task LookupWithAiReturnsReviewPartialWhenSuggestionsAreResolvedAsync()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var domain = new TrmDomain { Code = "TD001", Name = "Technology" };
        var capability = new TrmCapability { Code = "TP001", Name = "Identity", ParentDomain = domain, ParentDomainCode = domain.Code };
        var component = new TrmComponent { Code = "TC001", Name = "Directory Service", ParentCapability = capability, ParentCapabilityCode = capability.Code };
        var secondaryComponent = new TrmComponent { Code = "TC002", Name = "Authentication Service", ParentCapability = capability, ParentCapabilityCode = capability.Code };
        var product = new ProductCatalogItem { Name = "Active Directory" };
        await fixture.DbContext.AddRangeAsync(domain, capability, component, secondaryComponent, product);
        await fixture.SeedAiSettingsAsync();
        await fixture.DbContext.SaveChangesAsync();
        fixture.SetAiLookupResponseBody($"{{\"choices\":[{{\"message\":{{\"content\":\"summary: \\\"Suggested 2 TRM components.\\\"\\nsuggestions[2]{{component_id\\tconfidence\\treason}}:\\n  {component.Id}\\t0.95\\tMatches core identity and directory capabilities.\\n  {secondaryComponent.Id}\\t0.79\\tSupports sign-in flows but is less direct.\"}}}}]}}");

        using var controller = fixture.CreateController();
        var result = await controller.LookupWithAiAsync(product.Id);

        var view = Assert.IsType<PartialViewResult>(result);
        Assert.Equal("_AiMappingReviewContent", view.ViewName);
        var model = Assert.IsType<AiMappingReviewViewModel>(view.Model);
        Assert.Equal(product.Id, model.ProductId);
        Assert.True(model.LookupCompleted);
        Assert.Equal(2, model.Suggestions.Count);

        var primarySuggestion = model.Suggestions.Single(x => x.ComponentId == component.Id);
        Assert.Equal(component.DisplayLabel, primarySuggestion.ComponentLabel);
        Assert.Contains("95", primarySuggestion.ConfidenceLabel);
        Assert.True(primarySuggestion.Selected);

        var secondarySuggestion = model.Suggestions.Single(x => x.ComponentId == secondaryComponent.Id);
        Assert.Contains("79", secondarySuggestion.ConfidenceLabel);
        Assert.False(secondarySuggestion.Selected);
    }

    [Fact]
    public async Task CreateFromAiCreatesSelectedMappingsAsDraftsAsync()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var domain = new TrmDomain { Code = "TD001", Name = "Technology" };
        var capability = new TrmCapability { Code = "TP001", Name = "Identity", ParentDomain = domain, ParentDomainCode = domain.Code };
        var componentA = new TrmComponent { Code = "TC001", Name = "Directory Service", ParentCapability = capability, ParentCapabilityCode = capability.Code };
        var componentB = new TrmComponent { Code = "TC002", Name = "Authentication Service", ParentCapability = capability, ParentCapabilityCode = capability.Code };
        var product = new ProductCatalogItem { Name = "Active Directory" };
        await fixture.DbContext.AddRangeAsync(domain, capability, componentA, componentB, product);
        await fixture.DbContext.SaveChangesAsync();

        using var controller = fixture.CreateController();
        var result = await controller.CreateFromAiAsync(new AiMappingReviewViewModel
        {
            ProductId = product.Id,
            ProductName = product.Name,
            Suggestions =
            [
                new AiMappingSuggestionSelectionViewModel
                {
                    ComponentId = componentA.Id,
                    Confidence = 0.95m,
                    ConfidenceLabel = "95 %",
                    Reason = "Matches the core directory capability.",
                    Selected = true
                },
                new AiMappingSuggestionSelectionViewModel
                {
                    ComponentId = componentB.Id,
                    Confidence = 0.81m,
                    ConfidenceLabel = "81 %",
                    Reason = "Supports authentication and sign-in flows.",
                    Selected = true
                }
            ]
        });

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Details", redirect.ActionName);
        Assert.Equal("Products", redirect.ControllerName);

        var mappings = await fixture.DbContext.ProductMappings
            .OrderBy(x => x.TrmComponentId)
            .ToListAsync();

        Assert.Equal(2, mappings.Count);
        Assert.All(mappings, mapping => Assert.Equal(MappingStatus.Draft, mapping.MappingStatus));
        Assert.Contains("AI-assisted mapping review", mappings[0].MappingRationale);
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

    [Fact]
    public void NormalizeSelectionsReturnsEmptyListForNullAndSkipsDuplicates()
    {
        var method = typeof(MappingsController).GetMethod("NormalizeSelections", BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);
        var empty = Assert.IsType<List<string>>(method!.Invoke(null, [null])!);
        var normalized = Assert.IsType<List<string>>(method.Invoke(null, [DuplicateOwnerSelections])!);

        Assert.Empty(empty);
        Assert.Equal(["Team Blue", "Team Green"], normalized);
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
        public void SetAiLookupResponseBody(string value) => aiHttpMessageHandler.ResponseBody = value;

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

        public MappingsController CreateController()
        {
            var httpContext = new DefaultHttpContext();
            var controller = new MappingsController(
                DbContext,
                new AuditLogService(DbContext),
                new ComponentVersioningService(DbContext),
                new ConfigurableFieldService(DbContext),
                CreateAiProductMappingService());

            controller.ControllerContext = new ControllerContext
            {
                HttpContext = httpContext
            };
            controller.TempData = new TempDataDictionary(httpContext, new TestTempDataProvider());
            return controller;
        }

        public async Task SeedOwnerOptionsAsync()
        {
            await DbContext.ConfigurableFieldOptions.AddAsync(new ConfigurableFieldOption
            {
                FieldName = ConfigurableFieldNames.Owner,
                Value = "Team Blue",
                SortOrder = 1
            });
            await DbContext.SaveChangesAsync();
        }

        public async Task SeedAiSettingsAsync(int timeoutSeconds = AppSettingDefaults.AiMappingTimeoutSeconds)
        {
            await DbContext.AppSettings.AddRangeAsync(
                new AppSetting { Key = AppSettingKeys.AiMappingEndpoint, Value = "http://localhost:3000/api/chat/completions" },
                new AppSetting { Key = AppSettingKeys.AiMappingModel, Value = "gpt-oss:latest" },
                new AppSetting { Key = AppSettingKeys.AiMappingApiKey, Value = "lab-key" },
                new AppSetting { Key = AppSettingKeys.AiMappingIsEnabled, Value = "true" },
                new AppSetting { Key = AppSettingKeys.AiMappingTimeoutSeconds, Value = timeoutSeconds.ToString(CultureInfo.InvariantCulture) });
            await DbContext.SaveChangesAsync();
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

        public async ValueTask DisposeAsync()
        {
            aiHttpClient.Dispose();
            aiHttpMessageHandler.Dispose();
            await DbContext.DisposeAsync();
            await connection.DisposeAsync();
        }
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        public string ResponseBody { get; set; } =
            "{\"choices\":[{\"message\":{\"content\":\"summary: \\\"No suggestions\\\"\\nsuggestions[0]{component_id\\tconfidence\\treason}:\"}}]}";

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(ResponseBody, Encoding.UTF8, "application/json")
            });
    }

    private sealed class TestTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();

        public void SaveTempData(HttpContext context, IDictionary<string, object> values)
        {
        }
    }
}
