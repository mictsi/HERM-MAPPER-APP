using HERMMapperApp.Controllers;
using HERMMapperApp.Data;
using HERMMapperApp.Models;
using HERMMapperApp.Services;
using HERMMapperApp.ViewModels;
using System.Text.Json;
using System.Reflection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HERMMapperApp.Tests.Controllers;

public sealed class ServicesControllerTests
{
    private static readonly JsonSerializerOptions TestJsonSerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    [Fact]
    public async Task CreatePostPersistsServiceNormalizesSelectionsAndWritesAudit()
    {
        await using var fixture = await TestFixture.CreateAsync();
        await fixture.SeedConfigurableOptionsAsync();
        using var controller = fixture.CreateController();

        var result = await controller.Create(new ServiceEditViewModel
        {
            Name = "Security Operations",
            Description = "SOC tooling",
            Owner = " Team Blue ",
            LifecycleStatus = " Production "
        });

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(ServicesController.Connections), redirect.ActionName);

        var service = await fixture.DbContext.ServiceCatalogItems.SingleAsync();
        var audit = await fixture.DbContext.AuditLogEntries.SingleAsync();

        Assert.Equal("Security Operations", service.Name);
        Assert.Equal("Team Blue", service.Owner);
        Assert.Equal("Production", service.LifecycleStatus);
        Assert.Equal(service.Id, redirect.RouteValues!["id"]);
        Assert.Equal("Create", audit.Action);
    }

    [Fact]
    public async Task CreateGetPopulatesOwnerAndLifecycleOptions()
    {
        await using var fixture = await TestFixture.CreateAsync();
        await fixture.SeedConfigurableOptionsAsync();

        using var controller = fixture.CreateController();
        var result = await controller.Create();

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<ServiceEditViewModel>(view.Model);
        Assert.Contains(model.OwnerOptions, option => option.Value == "Team Blue");
        Assert.Contains(model.LifecycleStatusOptions, option => option.Value == "Production");
    }

    [Fact]
    public async Task CreatePostInvalidModelReturnsViewWithOptions()
    {
        await using var fixture = await TestFixture.CreateAsync();
        await fixture.SeedConfigurableOptionsAsync();

        using var controller = fixture.CreateController();
        controller.ModelState.AddModelError(nameof(ServiceEditViewModel.Name), "Required");
        var result = await controller.Create(new ServiceEditViewModel
        {
            Name = string.Empty,
            Owner = "Team Blue",
            LifecycleStatus = "Production"
        });

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<ServiceEditViewModel>(view.Model);
        Assert.Contains(model.OwnerOptions, option => option.Value == "Team Blue");
        Assert.Contains(model.LifecycleStatusOptions, option => option.Value == "Production");
    }

    [Fact]
    public async Task EditGetReturnsNotFoundWhenServiceMissing()
    {
        await using var fixture = await TestFixture.CreateAsync();

        using var controller = fixture.CreateController();
        var result = await controller.Edit(999);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task EditPostReturnsNotFoundWhenServiceMissing()
    {
        await using var fixture = await TestFixture.CreateAsync();

        using var controller = fixture.CreateController();
        var result = await controller.Edit(999, new ServiceEditViewModel
        {
            Name = "Missing",
            Owner = "Team Blue",
            LifecycleStatus = "Production"
        });

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task EditPostInvalidModelReturnsViewWithOptions()
    {
        await using var fixture = await TestFixture.CreateAsync();
        await fixture.SeedConfigurableOptionsAsync();
        fixture.DbContext.ServiceCatalogItems.Add(new ServiceCatalogItem
        {
            Name = "Payments",
            Owner = "Team Blue",
            LifecycleStatus = "Production"
        });
        await fixture.DbContext.SaveChangesAsync();
        var serviceId = await fixture.DbContext.ServiceCatalogItems.Select(x => x.Id).SingleAsync();

        using var controller = fixture.CreateController();
        controller.ModelState.AddModelError(nameof(ServiceEditViewModel.Name), "Required");
        var result = await controller.Edit(serviceId, new ServiceEditViewModel
        {
            Name = string.Empty,
            Owner = "Team Blue",
            LifecycleStatus = "Production"
        });

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<ServiceEditViewModel>(view.Model);
        Assert.Equal(serviceId, model.Id);
        Assert.Contains(model.OwnerOptions, option => option.Value == "Team Blue");
    }

    [Fact]
    public async Task EditGetReturnsExistingServiceView()
    {
        await using var fixture = await TestFixture.CreateAsync();
        await fixture.SeedConfigurableOptionsAsync();
        fixture.DbContext.ServiceCatalogItems.Add(new ServiceCatalogItem
        {
            Name = "Payments",
            Owner = "Team Blue",
            LifecycleStatus = "Production"
        });
        await fixture.DbContext.SaveChangesAsync();
        var serviceId = await fixture.DbContext.ServiceCatalogItems.Select(x => x.Id).SingleAsync();

        using var controller = fixture.CreateController();
        var result = await controller.Edit(serviceId);

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<ServiceEditViewModel>(view.Model);
        Assert.Equal(serviceId, model.Id);
    }

    [Fact]
    public async Task ConnectionsPostPersistsBranchingGraphAndWritesAudit()
    {
        await using var fixture = await TestFixture.CreateAsync();
        await fixture.SeedConfigurableOptionsAsync();

        var products = await fixture.SeedProductsAsync("Entry", "API", "Broker", "Portal");
        fixture.DbContext.ServiceCatalogItems.Add(new ServiceCatalogItem
        {
            Name = "Loopback Flow",
            Owner = "Team Blue",
            LifecycleStatus = "Production"
        });
        await fixture.DbContext.SaveChangesAsync();

        var serviceId = await fixture.DbContext.ServiceCatalogItems.Select(x => x.Id).SingleAsync();
        using var controller = fixture.CreateController();
        var canvasStateJson = JsonSerializer.Serialize(
            new
            {
                nodes = new[]
                {
                    new { productId = products[0].Id, x = 120, y = 80 },
                    new { productId = products[1].Id, x = 380, y = 80 },
                    new { productId = products[2].Id, x = 380, y = 280 },
                    new { productId = products[3].Id, x = 650, y = 280 }
                },
                connections = new[]
                {
                    new { fromProductId = products[0].Id, toProductId = products[1].Id },
                    new { fromProductId = products[0].Id, toProductId = products[2].Id },
                    new { fromProductId = products[2].Id, toProductId = products[3].Id }
                }
            },
            TestJsonSerializerOptions);

        var result = await controller.Connections(serviceId, new ServiceConnectionEditorViewModel
        {
            CanvasStateJson = canvasStateJson
        });

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(ServicesController.Connections), redirect.ActionName);

        var service = await fixture.DbContext.ServiceCatalogItems
            .Include(x => x.ProductLinks)
            .ThenInclude(x => x.ProductCatalogItem)
            .Include(x => x.ProductConnections)
            .SingleAsync();
        var audit = await fixture.DbContext.AuditLogEntries.SingleAsync();

        Assert.Equal(
            ["Entry", "API", "Broker", "Portal"],
            service.GetOrderedProductLinks().Select(x => x.ProductCatalogItem.Name).ToArray());
        Assert.Equal(3, service.ProductConnections.Count);
        Assert.NotNull(service.ConnectionLayoutJson);
        using (var document = JsonDocument.Parse(service.ConnectionLayoutJson!))
        {
            Assert.Equal(4, document.RootElement.GetProperty("nodes").GetArrayLength());
            Assert.Equal(3, document.RootElement.GetProperty("connections").GetArrayLength());
        }

        Assert.Equal("UpdateConnections", audit.Action);
    }

    [Fact]
    public async Task EditPostUpdatesServiceMetadataAndWritesAudit()
    {
        await using var fixture = await TestFixture.CreateAsync();
        await fixture.SeedConfigurableOptionsAsync();

        var service = new ServiceCatalogItem
        {
            Name = "Payments",
            Description = "Existing flow",
            Owner = "Team Blue",
            LifecycleStatus = "Trial"
        };

        fixture.DbContext.ServiceCatalogItems.Add(service);
        await fixture.DbContext.SaveChangesAsync();

        using var controller = fixture.CreateController();
        var result = await controller.Edit(service.Id, new ServiceEditViewModel
        {
            Name = "Payments Revised",
            Description = "Updated flow",
            Owner = " Team Green ",
            LifecycleStatus = " Production "
        });

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(ServicesController.Connections), redirect.ActionName);

        var updatedService = await fixture.DbContext.ServiceCatalogItems.SingleAsync(x => x.Id == service.Id);
        var audit = await fixture.DbContext.AuditLogEntries.SingleAsync();

        Assert.Equal("Payments Revised", updatedService.Name);
        Assert.Equal("Team Green", updatedService.Owner);
        Assert.Equal("Production", updatedService.LifecycleStatus);
        Assert.Equal("Update", audit.Action);
    }

    [Fact]
    public async Task ConnectionsGetBuildsRowsFromLegacyLinearFlow()
    {
        await using var fixture = await TestFixture.CreateAsync();

        var products = await fixture.SeedProductsAsync("Portal", "API", "Queue");
        fixture.DbContext.ServiceCatalogItems.Add(new ServiceCatalogItem
        {
            Name = "Customer Journey",
            Owner = "Team Blue",
            LifecycleStatus = "Production",
            ProductLinks =
            [
                new ServiceCatalogItemProduct { ProductCatalogItemId = products[0].Id, SortOrder = 1 },
                new ServiceCatalogItemProduct { ProductCatalogItemId = products[1].Id, SortOrder = 2 },
                new ServiceCatalogItemProduct { ProductCatalogItemId = products[2].Id, SortOrder = 3 }
            ]
        });
        await fixture.DbContext.SaveChangesAsync();

        var serviceId = await fixture.DbContext.ServiceCatalogItems.Select(x => x.Id).SingleAsync();
        using var connectionsController = fixture.CreateController();
        var result = await connectionsController.Connections(serviceId);

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<ServiceConnectionEditorViewModel>(view.Model);

        Assert.True(model.UsesLegacyFlow);
        Assert.Collection(
            model.ConnectionRows,
            row =>
            {
                Assert.Equal(products[0].Id, row.FromProductId);
                Assert.Equal(products[1].Id, row.ToProductId);
            },
            row =>
            {
                Assert.Equal(products[1].Id, row.FromProductId);
                Assert.Equal(products[2].Id, row.ToProductId);
            });
    }

    [Fact]
    public async Task ConnectionsGetBuildsRowsFromStoredGraphConnections()
    {
        await using var fixture = await TestFixture.CreateAsync();

        var products = await fixture.SeedProductsAsync("Portal", "API", "Queue");
        fixture.DbContext.ServiceCatalogItems.Add(new ServiceCatalogItem
        {
            Name = "Customer Journey",
            Owner = "Team Blue",
            LifecycleStatus = "Production",
            ProductConnections =
            [
                new ServiceCatalogItemConnection { FromProductCatalogItemId = products[0].Id, ToProductCatalogItemId = products[1].Id, SortOrder = 1 },
                new ServiceCatalogItemConnection { FromProductCatalogItemId = products[1].Id, ToProductCatalogItemId = products[2].Id, SortOrder = 2 }
            ]
        });
        await fixture.DbContext.SaveChangesAsync();

        var serviceId = await fixture.DbContext.ServiceCatalogItems.Select(x => x.Id).SingleAsync();
        using var controller = fixture.CreateController();
        var result = await controller.Connections(serviceId);

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<ServiceConnectionEditorViewModel>(view.Model);
        Assert.False(model.UsesLegacyFlow);
        Assert.Collection(
            model.ConnectionRows,
            row =>
            {
                Assert.Equal(products[0].Id, row.FromProductId);
                Assert.Equal(products[1].Id, row.ToProductId);
            },
            row =>
            {
                Assert.Equal(products[1].Id, row.FromProductId);
                Assert.Equal(products[2].Id, row.ToProductId);
            });
    }

    [Fact]
    public async Task ConnectionsGetReturnsNotFoundWhenServiceMissing()
    {
        await using var fixture = await TestFixture.CreateAsync();

        using var controller = fixture.CreateController();
        var result = await controller.Connections(999);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task ConnectionsGetReturnsStatusMessageFromTempData()
    {
        await using var fixture = await TestFixture.CreateAsync();
        fixture.DbContext.ServiceCatalogItems.Add(new ServiceCatalogItem
        {
            Name = "Payments",
            Owner = "Team Blue",
            LifecycleStatus = "Production"
        });
        await fixture.DbContext.SaveChangesAsync();
        var serviceId = await fixture.DbContext.ServiceCatalogItems.Select(x => x.Id).SingleAsync();

        using var controller = fixture.CreateController();
        controller.TempData["ServicesStatusMessage"] = "Saved";
        var result = await controller.Connections(serviceId);

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<ServiceConnectionEditorViewModel>(view.Model);
        Assert.Equal("Saved", model.StatusMessage);
    }

    [Fact]
    public async Task ConnectionsPostRejectsUnreadableCanvasState()
    {
        await using var fixture = await TestFixture.CreateAsync();
        fixture.DbContext.ServiceCatalogItems.Add(new ServiceCatalogItem
        {
            Name = "Payments",
            Owner = "Team Blue",
            LifecycleStatus = "Production"
        });
        await fixture.DbContext.SaveChangesAsync();
        var serviceId = await fixture.DbContext.ServiceCatalogItems.Select(x => x.Id).SingleAsync();

        using var controller = fixture.CreateController();
        var result = await controller.Connections(serviceId, new ServiceConnectionEditorViewModel
        {
            CanvasStateJson = "{not-json"
        });

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<ServiceConnectionEditorViewModel>(view.Model);
        Assert.Contains(controller.ModelState[nameof(ServiceConnectionEditorViewModel.CanvasStateJson)]!.Errors, error => error.ErrorMessage.Contains("could not be read", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(serviceId, model.ServiceId);
    }

    [Fact]
    public async Task ConnectionsPostRejectsNullCanvasStatePayload()
    {
        await using var fixture = await TestFixture.CreateAsync();
        fixture.DbContext.ServiceCatalogItems.Add(new ServiceCatalogItem
        {
            Name = "Payments",
            Owner = "Team Blue",
            LifecycleStatus = "Production"
        });
        await fixture.DbContext.SaveChangesAsync();
        var serviceId = await fixture.DbContext.ServiceCatalogItems.Select(x => x.Id).SingleAsync();

        using var controller = fixture.CreateController();
        var result = await controller.Connections(serviceId, new ServiceConnectionEditorViewModel
        {
            CanvasStateJson = "null"
        });

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<ServiceConnectionEditorViewModel>(view.Model);
        Assert.Contains(controller.ModelState[nameof(ServiceConnectionEditorViewModel.CanvasStateJson)]!.Errors, error => error.ErrorMessage.Contains("could not be read", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(serviceId, model.ServiceId);
    }

    [Fact]
    public async Task ConnectionsPostRejectsInvalidConnectionRows()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var products = await fixture.SeedProductsAsync("Portal", "API");
        fixture.DbContext.ServiceCatalogItems.Add(new ServiceCatalogItem
        {
            Name = "Payments",
            Owner = "Team Blue",
            LifecycleStatus = "Production"
        });
        await fixture.DbContext.SaveChangesAsync();
        var serviceId = await fixture.DbContext.ServiceCatalogItems.Select(x => x.Id).SingleAsync();

        using var controller = fixture.CreateController();
        var result = await controller.Connections(serviceId, new ServiceConnectionEditorViewModel
        {
            ConnectionRows =
            [
                new ServiceConnectionRowInputViewModel { FromProductId = products[0].Id, ToProductId = products[0].Id },
                new ServiceConnectionRowInputViewModel { FromProductId = products[1].Id, ToProductId = 9999 },
                new ServiceConnectionRowInputViewModel { FromProductId = products[1].Id, ToProductId = 9999 }
            ]
        });

        Assert.IsType<ViewResult>(result);
        Assert.Contains(controller.ModelState[nameof(ServiceConnectionEditorViewModel.ConnectionRows)]!.Errors, error => error.ErrorMessage.Contains("same row", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(controller.ModelState[nameof(ServiceConnectionEditorViewModel.ConnectionRows)]!.Errors, error => error.ErrorMessage.Contains("only be listed once", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(controller.ModelState[nameof(ServiceConnectionEditorViewModel.ConnectionRows)]!.Errors, error => error.ErrorMessage.Contains("could not be found", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ConnectionsPostUpdatesExistingConnectionsAndRemovesExtras()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var products = await fixture.SeedProductsAsync("Portal", "API", "Queue");
        fixture.DbContext.ServiceCatalogItems.Add(new ServiceCatalogItem
        {
            Name = "Payments",
            Owner = "Team Blue",
            LifecycleStatus = "Production",
            ProductConnections =
            [
                new ServiceCatalogItemConnection { FromProductCatalogItemId = products[0].Id, ToProductCatalogItemId = products[1].Id, SortOrder = 1 },
                new ServiceCatalogItemConnection { FromProductCatalogItemId = products[1].Id, ToProductCatalogItemId = products[2].Id, SortOrder = 2 }
            ],
            ProductLinks =
            [
                new ServiceCatalogItemProduct { ProductCatalogItemId = products[0].Id, SortOrder = 1 },
                new ServiceCatalogItemProduct { ProductCatalogItemId = products[1].Id, SortOrder = 2 },
                new ServiceCatalogItemProduct { ProductCatalogItemId = products[2].Id, SortOrder = 3 }
            ]
        });
        await fixture.DbContext.SaveChangesAsync();
        var serviceId = await fixture.DbContext.ServiceCatalogItems.Select(x => x.Id).SingleAsync();

        using var controller = fixture.CreateController();
        var result = await controller.Connections(serviceId, new ServiceConnectionEditorViewModel
        {
            ConnectionRows = [new ServiceConnectionRowInputViewModel { FromProductId = products[2].Id, ToProductId = products[0].Id }]
        });

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(ServicesController.Connections), redirect.ActionName);

        var service = await fixture.DbContext.ServiceCatalogItems
            .Include(x => x.ProductConnections.OrderBy(connection => connection.SortOrder))
            .Include(x => x.ProductLinks.OrderBy(link => link.SortOrder))
            .SingleAsync();
        var connections = service.ProductConnections.OrderBy(x => x.SortOrder).ToList();
        Assert.Single(connections);
        Assert.Equal(products[2].Id, connections[0].FromProductCatalogItemId);
        Assert.Equal(products[0].Id, connections[0].ToProductCatalogItemId);
        Assert.Equal([products[2].Id, products[0].Id], service.ProductLinks.OrderBy(x => x.SortOrder).Select(x => x.ProductCatalogItemId).ToArray());
    }

    [Fact]
    public async Task ConnectionsPostAddsNewConnectionsAndProductLinks()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var products = await fixture.SeedProductsAsync("Portal", "API", "Queue");
        fixture.DbContext.ServiceCatalogItems.Add(new ServiceCatalogItem
        {
            Name = "Payments",
            Owner = "Team Blue",
            LifecycleStatus = "Production",
            ProductConnections =
            [
                new ServiceCatalogItemConnection { FromProductCatalogItemId = products[0].Id, ToProductCatalogItemId = products[1].Id, SortOrder = 1 }
            ],
            ProductLinks =
            [
                new ServiceCatalogItemProduct { ProductCatalogItemId = products[0].Id, SortOrder = 1 },
                new ServiceCatalogItemProduct { ProductCatalogItemId = products[1].Id, SortOrder = 2 }
            ]
        });
        await fixture.DbContext.SaveChangesAsync();
        var serviceId = await fixture.DbContext.ServiceCatalogItems.Select(x => x.Id).SingleAsync();

        using var controller = fixture.CreateController();
        var result = await controller.Connections(serviceId, new ServiceConnectionEditorViewModel
        {
            ConnectionRows =
            [
                new ServiceConnectionRowInputViewModel { FromProductId = products[0].Id, ToProductId = products[1].Id },
                new ServiceConnectionRowInputViewModel { FromProductId = products[1].Id, ToProductId = products[2].Id }
            ]
        });

        Assert.IsType<RedirectToActionResult>(result);
        var service = await fixture.DbContext.ServiceCatalogItems
            .Include(x => x.ProductConnections.OrderBy(connection => connection.SortOrder))
            .Include(x => x.ProductLinks.OrderBy(link => link.SortOrder))
            .SingleAsync();
        Assert.Equal(2, service.ProductConnections.Count);
        Assert.Equal([products[0].Id, products[1].Id, products[2].Id], service.ProductLinks.OrderBy(x => x.SortOrder).Select(x => x.ProductCatalogItemId).ToArray());
    }

    [Fact]
    public async Task ConnectionsPostRejectsIncompleteConnectionRow()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var products = await fixture.SeedProductsAsync("Portal", "API");
        fixture.DbContext.ServiceCatalogItems.Add(new ServiceCatalogItem
        {
            Name = "Payments",
            Owner = "Team Blue",
            LifecycleStatus = "Production"
        });
        await fixture.DbContext.SaveChangesAsync();
        var serviceId = await fixture.DbContext.ServiceCatalogItems.Select(x => x.Id).SingleAsync();

        using var controller = fixture.CreateController();
        var result = await controller.Connections(serviceId, new ServiceConnectionEditorViewModel
        {
            ConnectionRows = [new ServiceConnectionRowInputViewModel { FromProductId = products[0].Id }]
        });

        Assert.IsType<ViewResult>(result);
        Assert.Contains(controller.ModelState[nameof(ServiceConnectionEditorViewModel.ConnectionRows)]!.Errors, error => error.ErrorMessage.Contains("Choose both products", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ConnectionsPostReturnsNotFoundWhenServiceMissing()
    {
        await using var fixture = await TestFixture.CreateAsync();

        using var controller = fixture.CreateController();
        var result = await controller.Connections(999, new ServiceConnectionEditorViewModel());

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task IndexFiltersBySearchOwnerAndLifecycleStatus()
    {
        await using var fixture = await TestFixture.CreateAsync();
        await fixture.SeedConfigurableOptionsAsync();

        var products = await fixture.SeedProductsAsync("Gateway", "Ledger", "Observability");
        await fixture.DbContext.ServiceCatalogItems.AddRangeAsync(
            new ServiceCatalogItem
            {
                Name = "Service A",
                Owner = "Team Blue",
                LifecycleStatus = "Production",
                ProductLinks =
                [
                    new ServiceCatalogItemProduct { ProductCatalogItemId = products[0].Id, SortOrder = 1 },
                    new ServiceCatalogItemProduct { ProductCatalogItemId = products[1].Id, SortOrder = 2 }
                ]
            },
            new ServiceCatalogItem
            {
                Name = "Service B",
                Owner = "Team Red",
                LifecycleStatus = "Trial",
                ProductLinks =
                [
                    new ServiceCatalogItemProduct { ProductCatalogItemId = products[2].Id, SortOrder = 1 },
                    new ServiceCatalogItemProduct { ProductCatalogItemId = products[1].Id, SortOrder = 2 }
                ]
            });
        await fixture.DbContext.SaveChangesAsync();

        using var indexController = fixture.CreateController();
        var result = await indexController.Index("gateway", "Team Blue", "Production", ServiceSortOptions.NameDesc);

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<ServicesIndexViewModel>(view.Model);

        Assert.Equal("Team Blue", model.Owner);
        Assert.Equal("Production", model.LifecycleStatus);
        Assert.Equal(ServiceSortOptions.NameDesc, model.Sort);
        Assert.Single(model.Services);
        Assert.Equal("Service A", model.Services[0].Name);
    }

    [Fact]
    public async Task IndexSortsByProductCountDescending()
    {
        await using var fixture = await TestFixture.CreateAsync();
        await fixture.SeedConfigurableOptionsAsync();

        var products = await fixture.SeedProductsAsync("Gateway", "Ledger", "Portal");
        await fixture.DbContext.ServiceCatalogItems.AddRangeAsync(
            new ServiceCatalogItem
            {
                Name = "Two Links",
                Owner = "Team Blue",
                LifecycleStatus = "Production",
                ProductConnections =
                [
                    new ServiceCatalogItemConnection { FromProductCatalogItemId = products[0].Id, ToProductCatalogItemId = products[1].Id, SortOrder = 1 },
                    new ServiceCatalogItemConnection { FromProductCatalogItemId = products[1].Id, ToProductCatalogItemId = products[2].Id, SortOrder = 2 }
                ]
            },
            new ServiceCatalogItem
            {
                Name = "One Link",
                Owner = "Team Blue",
                LifecycleStatus = "Production",
                ProductConnections =
                [
                    new ServiceCatalogItemConnection { FromProductCatalogItemId = products[0].Id, ToProductCatalogItemId = products[1].Id, SortOrder = 1 }
                ]
            });
        await fixture.DbContext.SaveChangesAsync();

        using var controller = fixture.CreateController();
        var result = await controller.Index(null, null, null, ServiceSortOptions.ProductCountDesc);

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<ServicesIndexViewModel>(view.Model);
        Assert.Equal(["Two Links", "One Link"], model.Services.Select(service => service.Name).ToArray());
    }

    [Fact]
    public async Task IndexExcludesDeletedServices()
    {
        await using var fixture = await TestFixture.CreateAsync();
        await fixture.SeedConfigurableOptionsAsync();

        await fixture.DbContext.ServiceCatalogItems.AddRangeAsync(
            new ServiceCatalogItem
            {
                Name = "Visible Service",
                Owner = "Team Blue",
                LifecycleStatus = "Production"
            },
            new ServiceCatalogItem
            {
                Name = "Deleted Service",
                Owner = "Team Blue",
                LifecycleStatus = "Production",
                IsDeleted = true,
                DeletedUtc = DateTime.UtcNow,
                DeletedReason = "Moved to trash from the service catalogue."
            });
        await fixture.DbContext.SaveChangesAsync();

        using var deletedIndexController = fixture.CreateController();
        var result = await deletedIndexController.Index(null, null, null, null);

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<ServicesIndexViewModel>(view.Model);
        Assert.Collection(model.Services, service => Assert.Equal("Visible Service", service.Name));
    }

    [Fact]
    public async Task IndexBuildsPreviewForMoreThanThreeProducts()
    {
        await using var fixture = await TestFixture.CreateAsync();
        await fixture.SeedConfigurableOptionsAsync();
        var products = await fixture.SeedProductsAsync("Portal", "API", "Queue", "Dashboard");
        fixture.DbContext.ServiceCatalogItems.Add(new ServiceCatalogItem
        {
            Name = "Customer Journey",
            Owner = "Team Blue",
            LifecycleStatus = "Production",
            ProductLinks =
            [
                new ServiceCatalogItemProduct { ProductCatalogItemId = products[0].Id, SortOrder = 1 },
                new ServiceCatalogItemProduct { ProductCatalogItemId = products[1].Id, SortOrder = 2 },
                new ServiceCatalogItemProduct { ProductCatalogItemId = products[2].Id, SortOrder = 3 },
                new ServiceCatalogItemProduct { ProductCatalogItemId = products[3].Id, SortOrder = 4 }
            ]
        });
        await fixture.DbContext.SaveChangesAsync();

        using var controller = fixture.CreateController();
        var result = await controller.Index(null, null, null, ServiceSortOptions.UpdatedAsc);

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<ServicesIndexViewModel>(view.Model);
        Assert.Contains("+1 more", model.Services.Single().ProductPreview, StringComparison.Ordinal);
    }

    [Fact]
    public async Task VisualizeReturnsGraphConnectionsInSavedOrder()
    {
        await using var fixture = await TestFixture.CreateAsync();

        var products = await fixture.SeedProductsAsync("Portal", "API", "Queue", "Dashboard");
        var service = new ServiceCatalogItem
        {
            Name = "Customer Journey",
            Owner = "Team Blue",
            LifecycleStatus = "Production",
            ProductConnections =
            [
                new ServiceCatalogItemConnection { FromProductCatalogItemId = products[0].Id, ToProductCatalogItemId = products[1].Id, SortOrder = 1 },
                new ServiceCatalogItemConnection { FromProductCatalogItemId = products[0].Id, ToProductCatalogItemId = products[2].Id, SortOrder = 2 },
                new ServiceCatalogItemConnection { FromProductCatalogItemId = products[2].Id, ToProductCatalogItemId = products[3].Id, SortOrder = 3 }
            ],
            ProductLinks =
            [
                new ServiceCatalogItemProduct { ProductCatalogItemId = products[0].Id, SortOrder = 1 },
                new ServiceCatalogItemProduct { ProductCatalogItemId = products[1].Id, SortOrder = 2 },
                new ServiceCatalogItemProduct { ProductCatalogItemId = products[2].Id, SortOrder = 3 },
                new ServiceCatalogItemProduct { ProductCatalogItemId = products[3].Id, SortOrder = 4 }
            ]
        };

        fixture.DbContext.ServiceCatalogItems.Add(service);
        await fixture.DbContext.SaveChangesAsync();

        using var visualizeController = fixture.CreateController();
        var result = await visualizeController.Visualize(service.Id);

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<ServiceVisualizationViewModel>(view.Model);

        Assert.True(model.UsesGraphConnections);
        Assert.Equal(["Portal", "API", "Queue", "Dashboard"], model.ProductNames.ToArray());
        Assert.Equal(3, model.Connections.Count);
        Assert.Equal("Portal", model.Connections[0].FromProductName);
        Assert.Equal("API", model.Connections[0].ToProductName);
        Assert.Equal("Portal", model.Connections[1].FromProductName);
        Assert.Equal("Queue", model.Connections[1].ToProductName);
        Assert.Equal("Queue", model.Connections[2].FromProductName);
        Assert.Equal("Dashboard", model.Connections[2].ToProductName);
    }

    [Fact]
    public async Task VisualizeReturnsNotFoundWhenServiceMissing()
    {
        await using var fixture = await TestFixture.CreateAsync();

        using var controller = fixture.CreateController();
        var result = await controller.Visualize(999);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task VisualizeBuildsLegacyConnectionsWhenGraphConnectionsAbsent()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var products = await fixture.SeedProductsAsync("Portal", "API", "Queue");
        fixture.DbContext.ServiceCatalogItems.Add(new ServiceCatalogItem
        {
            Name = "Legacy Flow",
            Owner = "Team Blue",
            LifecycleStatus = "Production",
            ProductLinks =
            [
                new ServiceCatalogItemProduct { ProductCatalogItemId = products[0].Id, SortOrder = 1 },
                new ServiceCatalogItemProduct { ProductCatalogItemId = products[1].Id, SortOrder = 2 },
                new ServiceCatalogItemProduct { ProductCatalogItemId = products[2].Id, SortOrder = 3 }
            ]
        });
        await fixture.DbContext.SaveChangesAsync();
        var serviceId = await fixture.DbContext.ServiceCatalogItems.Select(x => x.Id).SingleAsync();

        using var controller = fixture.CreateController();
        var result = await controller.Visualize(serviceId);

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<ServiceVisualizationViewModel>(view.Model);
        Assert.False(model.UsesGraphConnections);
        Assert.Equal(2, model.Connections.Count);
        Assert.Equal(products[0].Id, model.Connections[0].FromProductId);
        Assert.Equal(products[1].Id, model.Connections[0].ToProductId);
    }

    [Fact]
    public async Task VisualizeDisablesGraphLayoutForCyclicConnections()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var products = await fixture.SeedProductsAsync("Portal", "API");
        fixture.DbContext.ServiceCatalogItems.Add(new ServiceCatalogItem
        {
            Name = "Cyclic Flow",
            Owner = "Team Blue",
            LifecycleStatus = "Production",
            ProductConnections =
            [
                new ServiceCatalogItemConnection { FromProductCatalogItemId = products[0].Id, ToProductCatalogItemId = products[1].Id, SortOrder = 1 },
                new ServiceCatalogItemConnection { FromProductCatalogItemId = products[1].Id, ToProductCatalogItemId = products[0].Id, SortOrder = 2 }
            ],
            ProductLinks =
            [
                new ServiceCatalogItemProduct { ProductCatalogItemId = products[0].Id, SortOrder = 1 },
                new ServiceCatalogItemProduct { ProductCatalogItemId = products[1].Id, SortOrder = 2 }
            ]
        });
        await fixture.DbContext.SaveChangesAsync();
        var serviceId = await fixture.DbContext.ServiceCatalogItems.Select(x => x.Id).SingleAsync();

        using var controller = fixture.CreateController();
        var result = await controller.Visualize(serviceId);

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<ServiceVisualizationViewModel>(view.Model);
        Assert.True(model.UsesGraphConnections);
        Assert.False(model.SupportsGraphLayout);
    }

    [Fact]
    public async Task DeleteGetReturnsViewForExistingService()
    {
        await using var fixture = await TestFixture.CreateAsync();
        fixture.DbContext.ServiceCatalogItems.Add(new ServiceCatalogItem
        {
            Name = "Payments",
            Owner = "Team Blue",
            LifecycleStatus = "Production"
        });
        await fixture.DbContext.SaveChangesAsync();
        var serviceId = await fixture.DbContext.ServiceCatalogItems.Select(x => x.Id).SingleAsync();

        using var controller = fixture.CreateController();
        var result = await controller.Delete(serviceId);

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<ServiceCatalogItem>(view.Model);
        Assert.Equal(serviceId, model.Id);
    }

    [Fact]
    public async Task DeleteGetReturnsNotFoundWhenServiceMissing()
    {
        await using var fixture = await TestFixture.CreateAsync();

        using var controller = fixture.CreateController();
        var result = await controller.Delete(999);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task RestoreGetReturnsDeletedServicesAndStatusMessage()
    {
        await using var fixture = await TestFixture.CreateAsync();
        fixture.DbContext.ServiceCatalogItems.Add(new ServiceCatalogItem
        {
            Name = "Legacy Service",
            Owner = "Team Blue",
            LifecycleStatus = "Production",
            IsDeleted = true,
            DeletedUtc = DateTime.UtcNow,
            DeletedReason = "Moved to trash"
        });
        await fixture.DbContext.SaveChangesAsync();

        using var controller = fixture.CreateController();
        controller.TempData["ServicesStatusMessage"] = "Restored";
        var result = await controller.Restore();

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<ServiceRestoreViewModel>(view.Model);
        Assert.Equal("Restored", model.StatusMessage);
        Assert.Single(model.Services);
    }

    [Fact]
    public async Task RestoreDeletedReturnsNotFoundWhenServiceMissing()
    {
        await using var fixture = await TestFixture.CreateAsync();

        using var controller = fixture.CreateController();
        var result = await controller.RestoreDeleted(999);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task PermanentDeleteReturnsNotFoundWhenServiceMissing()
    {
        await using var fixture = await TestFixture.CreateAsync();

        using var controller = fixture.CreateController();
        var result = await controller.PermanentDelete(999);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task DeleteConfirmedSoftDeletesServiceAndWritesAudit()
    {
        await using var fixture = await TestFixture.CreateAsync();

        fixture.DbContext.ServiceCatalogItems.Add(new ServiceCatalogItem
        {
            Name = "Legacy Service",
            Owner = "Team Blue",
            LifecycleStatus = "Production"
        });
        await fixture.DbContext.SaveChangesAsync();

        var serviceId = await fixture.DbContext.ServiceCatalogItems.Select(x => x.Id).SingleAsync();
        using var deleteController = fixture.CreateController();
        var result = await deleteController.DeleteConfirmed(serviceId);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(ServicesController.Index), redirect.ActionName);
        var deletedService = await fixture.DbContext.ServiceCatalogItems.SingleAsync();
        Assert.True(deletedService.IsDeleted);
        Assert.NotNull(deletedService.DeletedUtc);
        Assert.Equal("Moved to trash from the service catalogue.", deletedService.DeletedReason);
        Assert.Equal("Delete", (await fixture.DbContext.AuditLogEntries.SingleAsync()).Action);
    }

    [Fact]
    public async Task DeleteConfirmedReturnsNotFoundWhenServiceMissing()
    {
        await using var fixture = await TestFixture.CreateAsync();

        using var controller = fixture.CreateController();
        var result = await controller.DeleteConfirmed(999);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task RestoreDeletedClearsSoftDeleteStateAndWritesAudit()
    {
        await using var fixture = await TestFixture.CreateAsync();

        fixture.DbContext.ServiceCatalogItems.Add(new ServiceCatalogItem
        {
            Name = "Legacy Service",
            Owner = "Team Blue",
            LifecycleStatus = "Production",
            IsDeleted = true,
            DeletedUtc = DateTime.UtcNow.AddDays(-1),
            DeletedReason = "Moved to trash from the service catalogue."
        });
        await fixture.DbContext.SaveChangesAsync();

        var serviceId = await fixture.DbContext.ServiceCatalogItems.Select(x => x.Id).SingleAsync();
        using var restoreController = fixture.CreateController();
        var result = await restoreController.RestoreDeleted(serviceId);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(ServicesController.Restore), redirect.ActionName);

        var restoredService = await fixture.DbContext.ServiceCatalogItems.SingleAsync();
        Assert.False(restoredService.IsDeleted);
        Assert.Null(restoredService.DeletedUtc);
        Assert.Null(restoredService.DeletedReason);
        Assert.Equal("Restore", (await fixture.DbContext.AuditLogEntries.SingleAsync()).Action);
    }

    [Fact]
    public async Task PermanentDeleteRemovesDeletedServiceAndWritesAudit()
    {
        await using var fixture = await TestFixture.CreateAsync();

        fixture.DbContext.ServiceCatalogItems.Add(new ServiceCatalogItem
        {
            Name = "Legacy Service",
            Owner = "Team Blue",
            LifecycleStatus = "Production",
            IsDeleted = true,
            DeletedUtc = DateTime.UtcNow.AddDays(-1),
            DeletedReason = "Moved to trash from the service catalogue."
        });
        await fixture.DbContext.SaveChangesAsync();

        var serviceId = await fixture.DbContext.ServiceCatalogItems.Select(x => x.Id).SingleAsync();
        using var permanentDeleteController = fixture.CreateController();
        var result = await permanentDeleteController.PermanentDelete(serviceId);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(ServicesController.Restore), redirect.ActionName);
        Assert.Empty(await fixture.DbContext.ServiceCatalogItems.ToListAsync());
        Assert.Equal("PermanentDelete", (await fixture.DbContext.AuditLogEntries.SingleAsync()).Action);
    }

    [Fact]
    public void GraphHelpersHandleEmptyCyclesAndSortFallbacks()
    {
        var canRenderAsGraph = GetStaticMethod("CanRenderAsGraph");
        var buildOrderedGraphProductIds = GetStaticMethod("BuildOrderedGraphProductIds");
        var applySort = GetStaticMethod("ApplySort");
        var normalizeSort = GetStaticMethod("NormalizeSort");

        Assert.False(Assert.IsType<bool>(canRenderAsGraph.Invoke(null, [new List<ServiceConnectionViewModel>()])!));

        var parameters = new object?[]
        {
            CreateConnectionPairs((1, 2), (2, 1)),
            null
        };
        var orderedIds = Assert.IsType<List<int>>(buildOrderedGraphProductIds.Invoke(null, parameters)!);
        var supportsGraphLayout = Assert.IsType<bool>(parameters[1]!);
        Assert.Equal([1, 2], orderedIds);
        Assert.False(supportsGraphLayout);

        var services = new[]
        {
            new ServiceCatalogItem { Name = "Zulu", Owner = "Team Blue", LifecycleStatus = "Trial", UpdatedUtc = new DateTime(2026, 3, 2, 0, 0, 0, DateTimeKind.Utc) },
            new ServiceCatalogItem { Name = "Alpha", Owner = "Team Green", LifecycleStatus = "Production", UpdatedUtc = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc) },
            new ServiceCatalogItem { Name = "Beta", Owner = "Team Blue", LifecycleStatus = "Production", UpdatedUtc = new DateTime(2026, 3, 3, 0, 0, 0, DateTimeKind.Utc) }
        };

        var ownerSorted = Assert.IsAssignableFrom<IEnumerable<ServiceCatalogItem>>(applySort.Invoke(null, [services, ServiceSortOptions.Owner])!);
        var lifecycleSorted = Assert.IsAssignableFrom<IEnumerable<ServiceCatalogItem>>(applySort.Invoke(null, [services, ServiceSortOptions.Lifecycle])!);
        var defaultSorted = Assert.IsAssignableFrom<IEnumerable<ServiceCatalogItem>>(applySort.Invoke(null, [services, "unknown"])!);

        Assert.Equal(["Beta", "Zulu", "Alpha"], ownerSorted.Select(service => service.Name).ToArray());
        Assert.Equal(["Alpha", "Beta", "Zulu"], lifecycleSorted.Select(service => service.Name).ToArray());
        Assert.Equal(["Beta", "Zulu", "Alpha"], defaultSorted.Select(service => service.Name).ToArray());
        Assert.Equal(ServiceSortOptions.Name, Assert.IsType<string>(normalizeSort.Invoke(null, [ServiceSortOptions.Name])!));
        Assert.Equal(ServiceSortOptions.Owner, Assert.IsType<string>(normalizeSort.Invoke(null, [ServiceSortOptions.Owner])!));
        Assert.Equal(ServiceSortOptions.Lifecycle, Assert.IsType<string>(normalizeSort.Invoke(null, [ServiceSortOptions.Lifecycle])!));
        Assert.Equal(ServiceSortOptions.UpdatedDesc, Assert.IsType<string>(normalizeSort.Invoke(null, ["other"])!));
    }

    [Fact]
    public void ConnectionNormalizationHelpersDropInvalidRowsAndNodes()
    {
        var normalizeConnectionRows = GetStaticMethod("NormalizeConnectionRows");
        var parseConnectionLayoutNodes = GetStaticMethod("ParseConnectionLayoutNodes");
        var normalizeCanvasNodes = GetStaticMethod("NormalizeCanvasNodes");

        var emptyRows = Assert.IsType<List<ServiceConnectionRowInputViewModel>>(normalizeConnectionRows.Invoke(null, [null])!);
        var normalizedRows = Assert.IsType<List<ServiceConnectionRowInputViewModel>>(normalizeConnectionRows.Invoke(null, [new ServiceConnectionRowInputViewModel[]
        {
            new() { FromProductId = null, ToProductId = null },
            new() { FromProductId = -1, ToProductId = 2 },
            new() { FromProductId = 3, ToProductId = 0 }
        }])!);

        Assert.Empty(emptyRows);
        Assert.Collection(
            normalizedRows,
            row =>
            {
                Assert.Null(row.FromProductId);
                Assert.Equal(2, row.ToProductId);
            },
            row =>
            {
                Assert.Equal(3, row.FromProductId);
                Assert.Null(row.ToProductId);
            });

        var invalidJsonNodes = Assert.IsType<List<ServiceConnectionCanvasNodeInputViewModel>>(parseConnectionLayoutNodes.Invoke(null, ["{bad-json"] )!);
        var nullJsonNodes = Assert.IsType<List<ServiceConnectionCanvasNodeInputViewModel>>(parseConnectionLayoutNodes.Invoke(null, ["null"])!);
        var normalizedNodes = Assert.IsType<List<ServiceConnectionCanvasNodeInputViewModel>>(normalizeCanvasNodes.Invoke(null, [new ServiceConnectionCanvasNodeInputViewModel[]
        {
            new() { ProductId = 0, X = 1, Y = 2 },
            new() { ProductId = 7, X = double.PositiveInfinity, Y = 12.345 },
            new() { ProductId = 7, X = 22, Y = 33 },
            new() { ProductId = 8, X = 15.678, Y = null }
        }])!);

        Assert.Empty(invalidJsonNodes);
        Assert.Empty(nullJsonNodes);
        Assert.Collection(
            normalizedNodes,
            node =>
            {
                Assert.Equal(7, node.ProductId);
                Assert.Null(node.X);
                Assert.Equal(12.34, node.Y);
            },
            node =>
            {
                Assert.Equal(8, node.ProductId);
                Assert.Equal(15.68, node.X);
                Assert.Null(node.Y);
            });
    }

    [Fact]
    public void BuildConnectionCanvasStateJsonMergesSavedNodesInputNodesAndConnectionProducts()
    {
        var buildConnectionCanvasStateJson = GetStaticMethod("BuildConnectionCanvasStateJson");
        var service = new ServiceCatalogItem
        {
            ConnectionLayoutJson = JsonSerializer.Serialize(
                new ServiceConnectionCanvasStateInputViewModel
                {
                    Nodes =
                    [
                        new ServiceConnectionCanvasNodeInputViewModel { ProductId = 1, X = 10, Y = null },
                        new ServiceConnectionCanvasNodeInputViewModel { ProductId = 2, X = 20, Y = 30 }
                    ]
                },
                TestJsonSerializerOptions)
        };
        var input = new ServiceConnectionEditorViewModel
        {
            CanvasNodes =
            [
                new ServiceConnectionCanvasNodeInputViewModel { ProductId = 1, X = null, Y = 40 },
                new ServiceConnectionCanvasNodeInputViewModel { ProductId = 0, X = 99, Y = 99 },
                new ServiceConnectionCanvasNodeInputViewModel { ProductId = 2, X = 50, Y = 60 }
            ],
            ConnectionRows =
            [
                new ServiceConnectionRowInputViewModel { FromProductId = 1, ToProductId = 3 },
                new ServiceConnectionRowInputViewModel { FromProductId = 3, ToProductId = 2 }
            ]
        };

        var json = Assert.IsType<string>(buildConnectionCanvasStateJson.Invoke(null, [service, input])!);
        using var document = JsonDocument.Parse(json);
        var nodes = document.RootElement.GetProperty("nodes");
        var connections = document.RootElement.GetProperty("connections");

        Assert.Equal(3, nodes.GetArrayLength());
        Assert.Equal(2, connections.GetArrayLength());
        Assert.Equal(1, nodes[0].GetProperty("productId").GetInt32());
        Assert.Equal(10, nodes[0].GetProperty("x").GetDouble());
        Assert.Equal(40, nodes[0].GetProperty("y").GetDouble());
        Assert.Equal(3, nodes[2].GetProperty("productId").GetInt32());
        Assert.True(nodes[2].GetProperty("x").ValueKind is JsonValueKind.Null);
        Assert.True(nodes[2].GetProperty("y").ValueKind is JsonValueKind.Null);
    }

    private static MethodInfo GetStaticMethod(string name)
    {
        var method = typeof(ServicesController).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        return method!;
    }

    private static Array CreateConnectionPairs(params (int FromProductId, int ToProductId)[] connections)
    {
        var type = typeof(ServicesController).GetNestedType("ConnectionPair", BindingFlags.NonPublic);
        Assert.NotNull(type);

        var array = Array.CreateInstance(type!, connections.Length);
        for (var index = 0; index < connections.Length; index++)
        {
            array.SetValue(Activator.CreateInstance(type!, connections[index].FromProductId, connections[index].ToProductId), index);
        }

        return array;
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

        public ServicesController CreateController()
        {
            var controller = new ServicesController(
                DbContext,
                new AuditLogService(DbContext),
                new ConfigurableFieldService(DbContext));

            controller.TempData = new TempDataDictionary(new DefaultHttpContext(), new TestTempDataProvider());
            return controller;
        }

        public async Task SeedConfigurableOptionsAsync()
        {
            await DbContext.ConfigurableFieldOptions.AddRangeAsync(
                new ConfigurableFieldOption { FieldName = ConfigurableFieldNames.Owner, Value = "Team Blue", SortOrder = 1 },
                new ConfigurableFieldOption { FieldName = ConfigurableFieldNames.Owner, Value = "Team Red", SortOrder = 2 },
                new ConfigurableFieldOption { FieldName = ConfigurableFieldNames.Owner, Value = "Team Green", SortOrder = 3 },
                new ConfigurableFieldOption { FieldName = ConfigurableFieldNames.LifecycleStatus, Value = "Production", SortOrder = 1 },
                new ConfigurableFieldOption { FieldName = ConfigurableFieldNames.LifecycleStatus, Value = "Trial", SortOrder = 2 });
            await DbContext.SaveChangesAsync();
        }

        public async Task<List<ProductCatalogItem>> SeedProductsAsync(params string[] names)
        {
            var products = names
                .Select(name => new ProductCatalogItem { Name = name })
                .ToList();

            await DbContext.ProductCatalogItems.AddRangeAsync(products);
            await DbContext.SaveChangesAsync();
            return products;
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
}
