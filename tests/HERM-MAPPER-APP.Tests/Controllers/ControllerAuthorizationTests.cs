using System.Reflection;
using HERMMapperApp.Controllers;
using HERMMapperApp.Models;
using Microsoft.AspNetCore.Authorization;
using Xunit;

namespace HERMMapperApp.Tests.Controllers;

public sealed class ControllerAuthorizationTests
{
    [Fact]
    public void CatalogueControllersUseCatalogueReadPolicy()
    {
        AssertClassPolicy<HomeController>(AppPolicies.CatalogueRead);
        AssertClassPolicy<ProductsController>(AppPolicies.CatalogueRead);
        AssertClassPolicy<ServicesController>(AppPolicies.CatalogueRead);
        AssertClassPolicy<ReferenceController>(AppPolicies.CatalogueRead);
        AssertClassPolicy<ReportsController>(AppPolicies.CatalogueRead);
    }

    [Fact]
    public void AdminControllersUseAdminOnlyPolicy()
    {
        AssertClassPolicy<MappingsController>(AppPolicies.AdminOnly);
        AssertClassPolicy<UsersController>(AppPolicies.AdminOnly);
        AssertClassPolicy<ConfigurationController>(AppPolicies.AdminOnly);
        AssertClassPolicy<ChangeLogController>(AppPolicies.AdminOnly);
    }

    [Fact]
    public void ProductWriteActionsRequireProductsAndServicesWritePolicy()
    {
        AssertMethodPolicy<ProductsController>(nameof(ProductsController.Create), AppPolicies.ProductsAndServicesWrite, 0);
        AssertMethodPolicy<ProductsController>(nameof(ProductsController.Create), AppPolicies.ProductsAndServicesWrite, 1);
        AssertMethodPolicy<ProductsController>(nameof(ProductsController.Edit), AppPolicies.ProductsAndServicesWrite, 1);
        AssertMethodPolicy<ProductsController>(nameof(ProductsController.Edit), AppPolicies.ProductsAndServicesWrite, 2);
        AssertMethodPolicy<ProductsController>(nameof(ProductsController.BulkEdit), AppPolicies.ProductsAndServicesWrite, 4);
        AssertMethodPolicy<ProductsController>(nameof(ProductsController.BulkEdit), AppPolicies.ProductsAndServicesWrite, 1);
        AssertMethodPolicy<ProductsController>(nameof(ProductsController.Delete), AppPolicies.ProductsAndServicesWrite, 1);
        AssertMethodPolicy<ProductsController>(nameof(ProductsController.DeleteConfirmed), AppPolicies.ProductsAndServicesWrite, 1);
        AssertMethodPolicy<ProductsController>(nameof(ProductsController.Restore), AppPolicies.AdminOnly, 0);
        AssertMethodPolicy<ProductsController>(nameof(ProductsController.RestoreDeleted), AppPolicies.AdminOnly, 1);
        AssertMethodPolicy<ProductsController>(nameof(ProductsController.PermanentDelete), AppPolicies.AdminOnly, 1);
    }

    [Fact]
    public void ServiceWriteActionsRequireProductsAndServicesWritePolicy()
    {
        AssertMethodPolicy<ServicesController>(nameof(ServicesController.Create), AppPolicies.ProductsAndServicesWrite, 0);
        AssertMethodPolicy<ServicesController>(nameof(ServicesController.Create), AppPolicies.ProductsAndServicesWrite, 1);
        AssertMethodPolicy<ServicesController>(nameof(ServicesController.Edit), AppPolicies.ProductsAndServicesWrite, 1);
        AssertMethodPolicy<ServicesController>(nameof(ServicesController.Edit), AppPolicies.ProductsAndServicesWrite, 2);
        AssertMethodPolicy<ServicesController>(nameof(ServicesController.Connections), AppPolicies.ProductsAndServicesWrite, 1);
        AssertMethodPolicy<ServicesController>(nameof(ServicesController.Connections), AppPolicies.ProductsAndServicesWrite, 2);
        AssertMethodPolicy<ServicesController>(nameof(ServicesController.Delete), AppPolicies.ProductsAndServicesWrite, 1);
        AssertMethodPolicy<ServicesController>(nameof(ServicesController.DeleteConfirmed), AppPolicies.ProductsAndServicesWrite, 1);
        AssertMethodPolicy<ServicesController>(nameof(ServicesController.Restore), AppPolicies.AdminOnly, 0);
        AssertMethodPolicy<ServicesController>(nameof(ServicesController.RestoreDeleted), AppPolicies.AdminOnly, 1);
        AssertMethodPolicy<ServicesController>(nameof(ServicesController.PermanentDelete), AppPolicies.AdminOnly, 1);
    }

    [Fact]
    public void ReferenceWriteActionsRequireAdminOnlyPolicy()
    {
        AssertMethodPolicy<ReferenceController>(nameof(ReferenceController.VerifyImportAsync), AppPolicies.AdminOnly, 1);
        AssertMethodPolicy<ReferenceController>(nameof(ReferenceController.ImportVerifiedAsync), AppPolicies.AdminOnly, 1);
        AssertMethodPolicy<ReferenceController>(nameof(ReferenceController.RestoreAsync), AppPolicies.AdminOnly, 0);
        AssertMethodPolicy<ReferenceController>(nameof(ReferenceController.DeleteComponentAsync), AppPolicies.AdminOnly, 1);
        AssertMethodPolicy<ReferenceController>(nameof(ReferenceController.RestoreComponentAsync), AppPolicies.AdminOnly, 1);
        AssertMethodPolicy<ReferenceController>(nameof(ReferenceController.PermanentlyDeleteComponentAsync), AppPolicies.AdminOnly, 1);
    }

    private static void AssertClassPolicy<TController>(string expectedPolicy)
    {
        var authorizeAttribute = typeof(TController).GetCustomAttribute<AuthorizeAttribute>();
        Assert.NotNull(authorizeAttribute);
        Assert.Equal(expectedPolicy, authorizeAttribute!.Policy);
    }

    private static void AssertMethodPolicy<TController>(string methodName, string expectedPolicy, int parameterCount)
    {
        var method = typeof(TController).GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Single(candidate => candidate.Name == methodName && candidate.GetParameters().Length == parameterCount);

        var authorizeAttribute = method.GetCustomAttribute<AuthorizeAttribute>();
        Assert.NotNull(authorizeAttribute);
        Assert.Equal(expectedPolicy, authorizeAttribute!.Policy);
    }
}
