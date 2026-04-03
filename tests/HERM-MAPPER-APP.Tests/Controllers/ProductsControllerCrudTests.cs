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
using System.Net;
using System.Net.Http;
using System.Text;
using Xunit;

namespace HERMMapperApp.Tests.Controllers;

public sealed class ProductsControllerCrudTests
{
    [Fact]
    public async Task IndexFiltersBySearchOwnerAndLifecycleStatusAsync()
    {
        await using var fixture = await TestFixture.CreateAsync();
        await fixture.SeedConfigurableOptionsAsync();

        await fixture.DbContext.ProductCatalogItems.AddRangeAsync(
            new ProductCatalogItem
            {
                Name = "Sentinel",
                Vendor = "Contoso",
                LifecycleStatus = "Production",
                Owners = [new ProductCatalogItemOwner { OwnerValue = "Team Blue" }]
            },
            new ProductCatalogItem
            {
                Name = "Atlas",
                Vendor = "Contoso",
                LifecycleStatus = "Trial",
                Owners = [new ProductCatalogItemOwner { OwnerValue = "Team Red" }]
            });
        await fixture.DbContext.SaveChangesAsync();

        using var controller = fixture.CreateController();
        var result = await controller.IndexAsync("Sent", ["team blue"], " Production ");

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<ProductsIndexViewModel>(view.Model);
        Assert.Equal("Sent", model.Search);
        Assert.Equal(["team blue"], model.SelectedOwners);
        Assert.Equal("Production", model.LifecycleStatus);
        Assert.Single(model.Products);
        Assert.Equal("Sentinel", model.Products[0].Name);
    }

    [Fact]
    public async Task CreateGetPopulatesOwnerAndLifecycleOptionsAsync()
    {
        await using var fixture = await TestFixture.CreateAsync();
        await fixture.SeedConfigurableOptionsAsync();

        using var controller = fixture.CreateController();
        var result = await controller.CreateAsync();

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<ProductEditViewModel>(view.Model);
        Assert.Contains(model.OwnerOptions, option => option.Value == "Team Blue");
        Assert.Contains(model.LifecycleStatusOptions, option => option.Value == "Production");
    }

    [Fact]
    public async Task CreatePostInvalidModelReturnsViewWithOptionsAsync()
    {
        await using var fixture = await TestFixture.CreateAsync();
        await fixture.SeedConfigurableOptionsAsync();
        using var controller = fixture.CreateController();
        controller.ModelState.AddModelError(nameof(ProductEditViewModel.Name), "Required");

        var result = await controller.CreateAsync(new ProductEditViewModel
        {
            Name = string.Empty,
            Owners = ["Team Blue"],
            LifecycleStatus = "Production"
        });

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<ProductEditViewModel>(view.Model);
        Assert.Contains(model.OwnerOptions, option => option.Value == "Team Blue");
        Assert.Contains(model.LifecycleStatusOptions, option => option.Value == "Production");
    }

    [Fact]
    public async Task DetailsReturnsProductWithMappingsAsync()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var domain = new TrmDomain { Code = "TD001", Name = "Technology" };
        var capability = new TrmCapability { Code = "TP001", Name = "Observability", ParentDomain = domain, ParentDomainCode = domain.Code };
        var component = new TrmComponent { Code = "TC001", Name = "Monitoring", ParentCapability = capability, ParentCapabilityCode = capability.Code };
        var product = new ProductCatalogItem { Name = "Sentinel" };
        var mapping = new ProductMapping { ProductCatalogItem = product, TrmDomain = domain, TrmCapability = capability, TrmComponent = component, MappingStatus = MappingStatus.Complete };
        await fixture.DbContext.AddRangeAsync(domain, capability, component, product, mapping);
        await fixture.DbContext.SaveChangesAsync();

        using var controller = fixture.CreateController();
        var result = await controller.DetailsAsync(product.Id);

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<ProductVisualizationViewModel>(view.Model);
        Assert.Equal(product.Id, model.Product.Id);
        Assert.Single(model.Product.Mappings);
        Assert.Equal(component.Id, Assert.Single(model.Product.Mappings).TrmComponentId);
        Assert.Single(model.Paths);
        Assert.Equal(component.DisplayLabel, model.Paths[0].ComponentLabel);
    }

    [Fact]
    public async Task DetailsSetsAiLookupFlagsFromStoredSettingsAsync()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var product = new ProductCatalogItem { Name = "Sentinel" };
        await fixture.DbContext.ProductCatalogItems.AddAsync(product);
        await fixture.DbContext.AppSettings.AddRangeAsync(
            new AppSetting { Key = AppSettingKeys.AiMappingEndpoint, Value = "http://localhost:3000/api/chat/completions" },
            new AppSetting { Key = AppSettingKeys.AiMappingModel, Value = "gpt-oss:latest" },
            new AppSetting { Key = AppSettingKeys.AiMappingApiKey, Value = "lab-key" },
            new AppSetting { Key = AppSettingKeys.AiMappingIsEnabled, Value = "true" });
        await fixture.DbContext.SaveChangesAsync();

        using var controller = fixture.CreateController();
        var result = await controller.DetailsAsync(product.Id);

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<ProductVisualizationViewModel>(view.Model);
        Assert.True(model.AiMappingLookupEnabled);
        Assert.True(model.AiMappingLookupConfigured);
        Assert.True(model.CanUseAiMappingLookup);
    }

    [Fact]
    public async Task DetailsReturnsNotFoundWhenProductMissingAsync()
    {
        await using var fixture = await TestFixture.CreateAsync();

        using var controller = fixture.CreateController();
        var result = await controller.DetailsAsync(999);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task EditGetReturnsNotFoundWhenProductMissingAsync()
    {
        await using var fixture = await TestFixture.CreateAsync();

        using var controller = fixture.CreateController();
        var result = await controller.EditAsync(999);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task EditPostInvalidModelReturnsViewWithOptionsAsync()
    {
        await using var fixture = await TestFixture.CreateAsync();
        await fixture.SeedConfigurableOptionsAsync();
        var product = new ProductCatalogItem { Name = "Sentinel" };
        fixture.DbContext.ProductCatalogItems.Add(product);
        await fixture.DbContext.SaveChangesAsync();

        using var controller = fixture.CreateController();
        controller.ModelState.AddModelError(nameof(ProductEditViewModel.Name), "Required");
        var result = await controller.EditAsync(product.Id, new ProductEditViewModel
        {
            Name = string.Empty,
            Owners = ["Team Blue"],
            LifecycleStatus = "Production"
        });

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<ProductEditViewModel>(view.Model);
        Assert.Equal(product.Id, model.Id);
        Assert.Contains(model.OwnerOptions, option => option.Value == "Team Blue");
    }

    [Fact]
    public async Task BulkEditGetRedirectsWhenNothingSelectedAsync()
    {
        await using var fixture = await TestFixture.CreateAsync();

        using var controller = fixture.CreateController();
        var result = await controller.BulkEditAsync(null, "atlas", ["Team Blue"], "Production");

        var redirect = Assert.IsType<RedirectResult>(result);
        Assert.Equal("/Products?search=atlas&lifecycleStatus=Production&owners=Team%20Blue", redirect.Url);
        Assert.Equal("Select one or more products before opening bulk edit.", controller.TempData["ProductsErrorMessage"]);
    }

    [Fact]
    public async Task DeleteGetReturnsProductViewAsync()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var product = new ProductCatalogItem { Name = "Sentinel", Owners = [new ProductCatalogItemOwner { OwnerValue = "Team Blue" }] };
        fixture.DbContext.ProductCatalogItems.Add(product);
        await fixture.DbContext.SaveChangesAsync();

        using var controller = fixture.CreateController();
        var result = await controller.DeleteAsync(product.Id);

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<ProductCatalogItem>(view.Model);
        Assert.Equal("Sentinel", model.Name);
    }

    [Fact]
    public async Task RestoreGetReturnsDeletedProductsAndStatusMessageAsync()
    {
        await using var fixture = await TestFixture.CreateAsync();
        fixture.DbContext.ProductCatalogItems.Add(new ProductCatalogItem
        {
            Name = "Sentinel",
            IsDeleted = true,
            DeletedUtc = DateTime.UtcNow,
            DeletedReason = "Moved to trash"
        });
        await fixture.DbContext.SaveChangesAsync();

        using var controller = fixture.CreateController();
        controller.TempData["ProductsStatusMessage"] = "Restored";
        var result = await controller.RestoreAsync();

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<ProductRestoreViewModel>(view.Model);
        Assert.Equal("Restored", model.StatusMessage);
        Assert.Single(model.Products);
    }
    [Fact]
    public async Task CreatePostPersistsProductNormalizesSelectionsAndWritesAuditAsync()
    {
        await using var fixture = await TestFixture.CreateAsync();
        await fixture.SeedConfigurableOptionsAsync();
        using var controller = fixture.CreateController();

        var result = await controller.CreateAsync(new ProductEditViewModel
        {
            Name = " Sentinel ",
            Vendor = "Microsoft",
            LifecycleStatus = " Production ",
            Owners = [" Team Blue ", "Team Blue", "Team Red", ""],
            Description = "Security analytics"
        });

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Index", redirect.ActionName);

        var product = await fixture.DbContext.ProductCatalogItems
            .Include(x => x.Owners)
            .SingleAsync();
        var audit = await fixture.DbContext.AuditLogEntries.SingleAsync();

        Assert.Equal(" Sentinel ", product.Name);
        Assert.Equal("Production", product.LifecycleStatus);
        Assert.Equal(["Team Blue", "Team Red"], product.GetOwnerValues());
        Assert.Equal("Create", audit.Action);
    }

    [Fact]
    public async Task EditPostUpdatesProductAndSynchronizesOwnersAsync()
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

        using var controller = fixture.CreateController();
        var result = await controller.EditAsync(product.Id, new ProductEditViewModel
        {
            Name = "Sentinel X",
            Vendor = "Contoso",
            LifecycleStatus = " Production ",
            Owners = ["Team Green", "Team Blue", "team green"],
            Description = "Updated"
        });

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Index", redirect.ActionName);

        var updatedProduct = await fixture.DbContext.ProductCatalogItems
            .Include(x => x.Owners)
            .SingleAsync(x => x.Id == product.Id);
        var audit = await fixture.DbContext.AuditLogEntries.SingleAsync();

        Assert.Equal("Sentinel X", updatedProduct.Name);
        Assert.Equal("Contoso", updatedProduct.Vendor);
        Assert.Equal("Production", updatedProduct.LifecycleStatus);
        Assert.Equal(["Team Blue", "Team Green"], updatedProduct.GetOwnerValues());
        Assert.Equal("Update", audit.Action);
    }

    [Fact]
    public async Task BulkEditGetReturnsSelectedProductsAndPreservesReturnFiltersAsync()
    {
        await using var fixture = await TestFixture.CreateAsync();
        await fixture.SeedConfigurableOptionsAsync();

        await fixture.DbContext.ProductCatalogItems.AddRangeAsync(
            new ProductCatalogItem
            {
                Name = "Sentinel",
                Vendor = "Microsoft",
                LifecycleStatus = "Trial",
                Owners = [new ProductCatalogItemOwner { OwnerValue = "Team Blue" }]
            },
            new ProductCatalogItem
            {
                Name = "Atlas",
                Vendor = "Adobe",
                LifecycleStatus = "Production",
                Owners = [new ProductCatalogItemOwner { OwnerValue = "Team Red" }]
            });
        await fixture.DbContext.SaveChangesAsync();

        var selectedIds = await fixture.DbContext.ProductCatalogItems
            .OrderBy(x => x.Id)
            .Select(x => x.Id)
            .ToArrayAsync();

        using var controller = fixture.CreateController();
        var result = await controller.BulkEditAsync(selectedIds, "atlas", ["Team Blue"], "Production");

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<ProductBulkEditViewModel>(view.Model);

        Assert.Equal("atlas", model.ReturnSearch);
        Assert.Equal(["Team Blue"], model.ReturnOwners);
        Assert.Equal("Production", model.ReturnLifecycleStatus);
        Assert.Equal(["Atlas", "Sentinel"], model.SelectedProducts.Select(x => x.Name).ToArray());
        Assert.Contains(model.OwnerOptions, option => option.Value == "Team Blue");
        Assert.Contains(model.LifecycleStatusOptions, option => option.Value == "Production");
    }

    [Fact]
    public async Task BulkEditPostUpdatesSelectedProductsAndWritesAuditAsync()
    {
        await using var fixture = await TestFixture.CreateAsync();
        await fixture.SeedConfigurableOptionsAsync();

        var sentinel = new ProductCatalogItem
        {
            Name = "Sentinel",
            Vendor = "Microsoft",
            LifecycleStatus = "Trial",
            Owners = [new ProductCatalogItemOwner { OwnerValue = "Team Blue" }]
        };
        var atlas = new ProductCatalogItem
        {
            Name = "Atlas",
            Vendor = "Adobe",
            LifecycleStatus = null,
            Owners = [new ProductCatalogItemOwner { OwnerValue = "Team Red" }]
        };
        var draft = new ProductCatalogItem
        {
            Name = "Draft Tool",
            Vendor = "Legacy",
            LifecycleStatus = "Pilot",
            Owners = [new ProductCatalogItemOwner { OwnerValue = "Team Red" }]
        };

        await fixture.DbContext.ProductCatalogItems.AddRangeAsync(sentinel, atlas, draft);
        await fixture.DbContext.SaveChangesAsync();

        using var controller = fixture.CreateController();
        var result = await controller.BulkEditAsync(new ProductBulkEditViewModel
        {
            SelectedProductIds = [sentinel.Id, atlas.Id],
            ReturnSearch = "prod",
            ReturnOwners = ["Team Blue"],
            ReturnLifecycleStatus = "Trial",
            ApplyVendor = true,
            Vendor = " Contoso ",
            ApplyOwners = true,
            OwnerUpdateMode = ProductBulkOwnerUpdateModes.Replace,
            Owners = ["Team Green", "team green"],
            ApplyLifecycleStatus = true,
            LifecycleStatus = " Production "
        });

        var redirect = Assert.IsType<RedirectResult>(result);
        Assert.Equal("/Products?search=prod&lifecycleStatus=Trial&owners=Team%20Blue", redirect.Url);
        Assert.Equal("Updated 2 of 2 selected product(s).", controller.TempData["ProductsStatusMessage"]);

        var updatedProducts = await fixture.DbContext.ProductCatalogItems
            .Include(x => x.Owners)
            .OrderBy(x => x.Name)
            .ToListAsync();
        var audit = await fixture.DbContext.AuditLogEntries.SingleAsync();

        Assert.Equal("Contoso", updatedProducts[0].Vendor);
        Assert.Equal("Production", updatedProducts[0].LifecycleStatus);
        Assert.Equal(["Team Green"], updatedProducts[0].GetOwnerValues());

        Assert.Equal("Contoso", updatedProducts[2].Vendor);
        Assert.Equal("Production", updatedProducts[2].LifecycleStatus);
        Assert.Equal(["Team Green"], updatedProducts[2].GetOwnerValues());

        Assert.Equal("Legacy", updatedProducts[1].Vendor);
        Assert.Equal("Pilot", updatedProducts[1].LifecycleStatus);
        Assert.Equal(["Team Red"], updatedProducts[1].GetOwnerValues());

        Assert.Equal("BulkUpdate", audit.Action);
        Assert.Contains("Vendor", audit.Details);
        Assert.Contains("Owners (replace)", audit.Details);
        Assert.Contains("Lifecycle status", audit.Details);
    }

    [Fact]
    public async Task BulkEditPostAppendsOwnersWhenAppendModeIsSelectedAsync()
    {
        await using var fixture = await TestFixture.CreateAsync();
        await fixture.SeedConfigurableOptionsAsync();

        var sentinel = new ProductCatalogItem
        {
            Name = "Sentinel",
            Owners =
            [
                new ProductCatalogItemOwner { OwnerValue = "Team Blue" },
                new ProductCatalogItemOwner { OwnerValue = "Team Red" }
            ]
        };
        var atlas = new ProductCatalogItem
        {
            Name = "Atlas",
            Owners = [new ProductCatalogItemOwner { OwnerValue = "Team Red" }]
        };

        await fixture.DbContext.ProductCatalogItems.AddRangeAsync(sentinel, atlas);
        await fixture.DbContext.SaveChangesAsync();

        using var controller = fixture.CreateController();
        var result = await controller.BulkEditAsync(new ProductBulkEditViewModel
        {
            SelectedProductIds = [sentinel.Id, atlas.Id],
            ApplyOwners = true,
            OwnerUpdateMode = ProductBulkOwnerUpdateModes.Append,
            Owners = ["Team Green", "team red"]
        });

        var redirect = Assert.IsType<RedirectResult>(result);
        Assert.Equal("/Products", redirect.Url);

        var updatedProducts = await fixture.DbContext.ProductCatalogItems
            .Include(x => x.Owners)
            .OrderBy(x => x.Name)
            .ToListAsync();
        var audit = await fixture.DbContext.AuditLogEntries.SingleAsync();

        Assert.Equal(["Team Green", "Team Red"], updatedProducts[0].GetOwnerValues());
        Assert.Equal(["Team Blue", "Team Green", "Team Red"], updatedProducts[1].GetOwnerValues());
        Assert.Contains("Owners (append)", audit.Details);
    }

    [Fact]
    public async Task VisualizeReturnsDistinctPathsUsingFallbackHierarchyAsync()
    {
        await using var fixture = await TestFixture.CreateAsync();

        var domain = new TrmDomain { Code = "TD001", Name = "Technology" };
        var capability = new TrmCapability { Code = "TP001", Name = "Observability", ParentDomain = domain, ParentDomainCode = domain.Code };
        var component = new TrmComponent { Code = "TC001", Name = "Monitoring", ParentCapability = capability, ParentCapabilityCode = capability.Code };
        var product = new ProductCatalogItem { Name = "Sentinel" };

        await fixture.DbContext.AddRangeAsync(domain, capability, component, product);
        await fixture.DbContext.SaveChangesAsync();

        await fixture.DbContext.ProductMappings.AddRangeAsync(
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

        using var visualizeController = fixture.CreateController();
        var result = await visualizeController.VisualizeAsync(product.Id);

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<ProductVisualizationViewModel>(view.Model);

        Assert.Equal(2, model.Paths.Count);
        Assert.Contains(model.Paths, path => path.ComponentLabel == "-" && path.Status == "Draft");
        Assert.Contains(model.Paths, path => path.ComponentLabel == "TC001 Monitoring" && path.Status == "Complete");
    }

    [Fact]
    public async Task ShowDependenciesReturnsIncomingAndOutgoingDependenciesFromServiceDataAsync()
    {
        await using var fixture = await TestFixture.CreateAsync();

        var sentinel = new ProductCatalogItem { Name = "Sentinel" };
        var portal = new ProductCatalogItem { Name = "Portal" };
        var broker = new ProductCatalogItem { Name = "Broker" };
        var archive = new ProductCatalogItem { Name = "Archive", IsDeleted = true };

        await fixture.DbContext.ProductCatalogItems.AddRangeAsync(sentinel, portal, broker, archive);
        await fixture.DbContext.SaveChangesAsync();

        await fixture.DbContext.ServiceCatalogItems.AddRangeAsync(
            new ServiceCatalogItem
            {
                Name = "Checkout",
                Owner = "Team Blue",
                LifecycleStatus = "Production",
                ProductConnections =
                [
                    new ServiceCatalogItemConnection
                    {
                        FromProductCatalogItemId = portal.Id,
                        ToProductCatalogItemId = sentinel.Id,
                        SortOrder = 1
                    },
                    new ServiceCatalogItemConnection
                    {
                        FromProductCatalogItemId = sentinel.Id,
                        ToProductCatalogItemId = broker.Id,
                        SortOrder = 2
                    }
                ]
            },
            new ServiceCatalogItem
            {
                Name = "Legacy handoff",
                Owner = "Team Red",
                LifecycleStatus = "Trial",
                ProductLinks =
                [
                    new ServiceCatalogItemProduct { ProductCatalogItemId = archive.Id, SortOrder = 1 },
                    new ServiceCatalogItemProduct { ProductCatalogItemId = sentinel.Id, SortOrder = 2 },
                    new ServiceCatalogItemProduct { ProductCatalogItemId = portal.Id, SortOrder = 3 }
                ]
            },
            new ServiceCatalogItem
            {
                Name = "Deleted flow",
                Owner = "Team Green",
                LifecycleStatus = "Production",
                IsDeleted = true,
                ProductConnections =
                [
                    new ServiceCatalogItemConnection
                    {
                        FromProductCatalogItemId = broker.Id,
                        ToProductCatalogItemId = sentinel.Id,
                        SortOrder = 1
                    }
                ]
            });
        await fixture.DbContext.SaveChangesAsync();

        using var controller = fixture.CreateController();
        var result = await controller.ShowDependenciesAsync(sentinel.Id);

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<ProductDependenciesViewModel>(view.Model);

        Assert.Equal(sentinel.Id, model.Product.Id);
        Assert.Equal(2, model.IncomingDependencies.Count);
        Assert.Equal(2, model.OutgoingDependencies.Count);

        Assert.Collection(
            model.IncomingDependencies,
            dependency =>
            {
                Assert.Equal("Checkout", dependency.ServiceName);
                Assert.Equal("Portal", dependency.RelatedProductLabel);
                Assert.True(dependency.CanOpenRelatedProduct);
            },
            dependency =>
            {
                Assert.Equal("Legacy handoff", dependency.ServiceName);
                Assert.Equal("Archive [deleted]", dependency.RelatedProductLabel);
                Assert.False(dependency.CanOpenRelatedProduct);
            });

        Assert.Collection(
            model.OutgoingDependencies,
            dependency =>
            {
                Assert.Equal("Checkout", dependency.ServiceName);
                Assert.Equal("Broker", dependency.RelatedProductLabel);
            },
            dependency =>
            {
                Assert.Equal("Legacy handoff", dependency.ServiceName);
                Assert.Equal("Portal", dependency.RelatedProductLabel);
            });
    }

    [Fact]
    public async Task ShowDependenciesReturnsNotFoundWhenProductMissingAsync()
    {
        await using var fixture = await TestFixture.CreateAsync();

        using var controller = fixture.CreateController();
        var result = await controller.ShowDependenciesAsync(999);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task DeleteConfirmedSoftDeletesProductAndWritesAuditAsync()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var product = new ProductCatalogItem { Name = "Sentinel" };
        fixture.DbContext.ProductCatalogItems.Add(product);
        await fixture.DbContext.SaveChangesAsync();

        using var deleteController = fixture.CreateController();
        var result = await deleteController.DeleteConfirmedAsync(product.Id);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Index", redirect.ActionName);
        var deletedProduct = await fixture.DbContext.ProductCatalogItems.SingleAsync();
        Assert.True(deletedProduct.IsDeleted);
        Assert.NotNull(deletedProduct.DeletedUtc);
        Assert.Equal("Moved to trash from the product catalogue.", deletedProduct.DeletedReason);
        Assert.Equal("Delete", (await fixture.DbContext.AuditLogEntries.SingleAsync()).Action);
    }

    [Fact]
    public async Task RestoreDeletedClearsSoftDeleteStateAndWritesAuditAsync()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var product = new ProductCatalogItem
        {
            Name = "Sentinel",
            IsDeleted = true,
            DeletedUtc = DateTime.UtcNow.AddDays(-1),
            DeletedReason = "Moved to trash from the product catalogue."
        };
        fixture.DbContext.ProductCatalogItems.Add(product);
        await fixture.DbContext.SaveChangesAsync();

        using var restoreController = fixture.CreateController();
        var result = await restoreController.RestoreDeletedAsync(product.Id);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Restore", redirect.ActionName);

        var restoredProduct = await fixture.DbContext.ProductCatalogItems.SingleAsync();
        Assert.False(restoredProduct.IsDeleted);
        Assert.Null(restoredProduct.DeletedUtc);
        Assert.Null(restoredProduct.DeletedReason);
        Assert.Equal("Restore", (await fixture.DbContext.AuditLogEntries.SingleAsync()).Action);
    }

    [Fact]
    public async Task PermanentDeleteRemovesDeletedProductAndWritesAuditAsync()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var product = new ProductCatalogItem
        {
            Name = "Sentinel",
            IsDeleted = true,
            DeletedUtc = DateTime.UtcNow.AddDays(-1),
            DeletedReason = "Moved to trash from the product catalogue."
        };
        fixture.DbContext.ProductCatalogItems.Add(product);
        await fixture.DbContext.SaveChangesAsync();

        using var permanentDeleteController = fixture.CreateController();
        var result = await permanentDeleteController.PermanentDeleteAsync(product.Id);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Restore", redirect.ActionName);
        Assert.Empty(await fixture.DbContext.ProductCatalogItems.ToListAsync());
        Assert.Equal("PermanentDelete", (await fixture.DbContext.AuditLogEntries.SingleAsync()).Action);
    }

    [Fact]
    public async Task PermanentDeleteRemovesDependentServiceConnectionsBeforeDeletingProductAsync()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var service = new ServiceCatalogItem
        {
            Name = "Payments",
            Owner = "Platform",
            LifecycleStatus = "Production"
        };
        var fromProduct = new ProductCatalogItem { Name = "Atlas" };
        var deletedProduct = new ProductCatalogItem
        {
            Name = "Sentinel",
            IsDeleted = true,
            DeletedUtc = DateTime.UtcNow.AddDays(-1),
            DeletedReason = "Moved to trash from the product catalogue."
        };
        var outgoingConnection = new ServiceCatalogItemConnection
        {
            ServiceCatalogItem = service,
            FromProductCatalogItem = deletedProduct,
            ToProductCatalogItem = fromProduct,
            SortOrder = 1
        };
        var incomingConnection = new ServiceCatalogItemConnection
        {
            ServiceCatalogItem = service,
            FromProductCatalogItem = fromProduct,
            ToProductCatalogItem = deletedProduct,
            SortOrder = 2
        };

        await fixture.DbContext.AddRangeAsync(service, fromProduct, deletedProduct, outgoingConnection, incomingConnection);
        await fixture.DbContext.SaveChangesAsync();

        using var permanentDeleteController = fixture.CreateController();
        var result = await permanentDeleteController.PermanentDeleteAsync(deletedProduct.Id);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Restore", redirect.ActionName);
        Assert.DoesNotContain(await fixture.DbContext.ProductCatalogItems.ToListAsync(), x => x.Id == deletedProduct.Id);
        Assert.Empty(await fixture.DbContext.ServiceCatalogItemConnections.ToListAsync());
        Assert.Equal("PermanentDelete", (await fixture.DbContext.AuditLogEntries.SingleAsync()).Action);
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

        public ProductsController CreateController() =>
            CreateProductsController();

        public ProductsController CreateProductsController()
        {
            var appSettingsService = new AppSettingsService(DbContext);
            var controller = new ProductsController(
                DbContext,
                new AuditLogService(DbContext),
                new ConfigurableFieldService(DbContext),
                new AiProductMappingService(
                    DbContext,
                    appSettingsService,
                    new ProtectedSettingsService(
                        new EphemeralDataProtectionProvider(),
                        appSettingsService,
                        NullLogger<ProtectedSettingsService>.Instance),
                    new AuditLogService(DbContext),
                    aiHttpClient,
                    NullLogger<AiProductMappingService>.Instance));

            controller.TempData = new TempDataDictionary(new DefaultHttpContext(), new TestTempDataProvider());
            return controller;
        }

        public async Task SeedConfigurableOptionsAsync()
        {
            await DbContext.ConfigurableFieldOptions.AddRangeAsync(
                new ConfigurableFieldOption { FieldName = ConfigurableFieldNames.Owner, Value = "Team Blue", SortOrder = 1 },
                new ConfigurableFieldOption { FieldName = ConfigurableFieldNames.Owner, Value = "Team Red", SortOrder = 2 },
                new ConfigurableFieldOption { FieldName = ConfigurableFieldNames.Owner, Value = "Team Green", SortOrder = 3 },
                new ConfigurableFieldOption { FieldName = ConfigurableFieldNames.LifecycleStatus, Value = "Production", SortOrder = 1 });
            await DbContext.SaveChangesAsync();
        }

        public async ValueTask DisposeAsync()
        {
            aiHttpClient.Dispose();
            aiHttpMessageHandler.Dispose();
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

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"choices\":[{\"message\":{\"content\":\"summary: \\\"No suggestions\\\"\\nsuggestions[0]{component_id\\tconfidence\\treason}:\"}}]}", Encoding.UTF8, "application/json")
            });
    }
}
