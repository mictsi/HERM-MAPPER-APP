using System.Reflection;
using HERM_MAPPER_APP.Controllers;
using HERM_MAPPER_APP.Models;
using Microsoft.AspNetCore.Authorization;
using Xunit;

namespace HERM_MAPPER_APP.Tests.Controllers;

public sealed class ControllerAuthorizationTests
{
    [Fact]
    public void CatalogueControllers_UseCatalogueReadPolicy()
    {
        AssertClassPolicy<HomeController>(AppPolicies.CatalogueRead);
        AssertClassPolicy<ProductsController>(AppPolicies.CatalogueRead);
        AssertClassPolicy<ServicesController>(AppPolicies.CatalogueRead);
        AssertClassPolicy<ReferenceController>(AppPolicies.CatalogueRead);
        AssertClassPolicy<ReportsController>(AppPolicies.CatalogueRead);
    }

    [Fact]
    public void AdminControllers_UseAdminOnlyPolicy()
    {
        AssertClassPolicy<MappingsController>(AppPolicies.AdminOnly);
        AssertClassPolicy<UsersController>(AppPolicies.AdminOnly);
        AssertClassPolicy<ConfigurationController>(AppPolicies.AdminOnly);
        AssertClassPolicy<ChangeLogController>(AppPolicies.AdminOnly);
    }

    [Fact]
    public void ProductWriteActions_RequireProductsAndServicesWritePolicy()
    {
        AssertMethodPolicy<ProductsController>(nameof(ProductsController.Create), AppPolicies.ProductsAndServicesWrite, 0);
        AssertMethodPolicy<ProductsController>(nameof(ProductsController.Create), AppPolicies.ProductsAndServicesWrite, 1);
        AssertMethodPolicy<ProductsController>(nameof(ProductsController.Edit), AppPolicies.ProductsAndServicesWrite, 1);
        AssertMethodPolicy<ProductsController>(nameof(ProductsController.Edit), AppPolicies.ProductsAndServicesWrite, 2);
        AssertMethodPolicy<ProductsController>(nameof(ProductsController.BulkEdit), AppPolicies.ProductsAndServicesWrite, 4);
        AssertMethodPolicy<ProductsController>(nameof(ProductsController.BulkEdit), AppPolicies.ProductsAndServicesWrite, 1);
        AssertMethodPolicy<ProductsController>(nameof(ProductsController.Delete), AppPolicies.ProductsAndServicesWrite, 1);
        AssertMethodPolicy<ProductsController>(nameof(ProductsController.DeleteConfirmed), AppPolicies.ProductsAndServicesWrite, 1);
    }

    [Fact]
    public void ServiceWriteActions_RequireProductsAndServicesWritePolicy()
    {
        AssertMethodPolicy<ServicesController>(nameof(ServicesController.Create), AppPolicies.ProductsAndServicesWrite, 0);
        AssertMethodPolicy<ServicesController>(nameof(ServicesController.Create), AppPolicies.ProductsAndServicesWrite, 1);
        AssertMethodPolicy<ServicesController>(nameof(ServicesController.Edit), AppPolicies.ProductsAndServicesWrite, 1);
        AssertMethodPolicy<ServicesController>(nameof(ServicesController.Edit), AppPolicies.ProductsAndServicesWrite, 2);
        AssertMethodPolicy<ServicesController>(nameof(ServicesController.Delete), AppPolicies.ProductsAndServicesWrite, 1);
        AssertMethodPolicy<ServicesController>(nameof(ServicesController.DeleteConfirmed), AppPolicies.ProductsAndServicesWrite, 1);
    }

    [Fact]
    public void ReferenceWriteActions_RequireAdminOnlyPolicy()
    {
        AssertMethodPolicy<ReferenceController>(nameof(ReferenceController.VerifyImport), AppPolicies.AdminOnly, 1);
        AssertMethodPolicy<ReferenceController>(nameof(ReferenceController.ImportVerified), AppPolicies.AdminOnly, 1);
        AssertMethodPolicy<ReferenceController>(nameof(ReferenceController.DeleteComponent), AppPolicies.AdminOnly, 1);
        AssertMethodPolicy<ReferenceController>(nameof(ReferenceController.RestoreComponent), AppPolicies.AdminOnly, 1);
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