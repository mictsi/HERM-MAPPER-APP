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
        AssertClassPolicy<ApplicationsController>(AppPolicies.CatalogueRead);
        AssertClassPolicy<CapabilitiesController>(AppPolicies.CatalogueRead);
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
        AssertMethodPolicy<ProductsController>("CreateAsync", AppPolicies.ProductsAndServicesWrite, 0);
        AssertMethodPolicy<ProductsController>("CreateAsync", AppPolicies.ProductsAndServicesWrite, 1);
        AssertMethodPolicy<ProductsController>("EditAsync", AppPolicies.ProductsAndServicesWrite, 1);
        AssertMethodPolicy<ProductsController>("EditAsync", AppPolicies.ProductsAndServicesWrite, 2);
        AssertMethodPolicy<ProductsController>("BulkEditAsync", AppPolicies.ProductsAndServicesWrite, 4);
        AssertMethodPolicy<ProductsController>("BulkEditAsync", AppPolicies.ProductsAndServicesWrite, 1);
        AssertMethodPolicy<ProductsController>("DeleteAsync", AppPolicies.ProductsAndServicesWrite, 1);
        AssertMethodPolicy<ProductsController>("DeleteConfirmedAsync", AppPolicies.ProductsAndServicesWrite, 1);
        AssertMethodPolicy<ProductsController>("RestoreAsync", AppPolicies.AdminOnly, 0);
        AssertMethodPolicy<ProductsController>("RestoreDeletedAsync", AppPolicies.AdminOnly, 1);
        AssertMethodPolicy<ProductsController>("PermanentDeleteAsync", AppPolicies.AdminOnly, 1);
    }

    [Fact]
    public void ServiceWriteActionsRequireProductsAndServicesWritePolicy()
    {
        AssertMethodPolicy<ServicesController>("CreateAsync", AppPolicies.ProductsAndServicesWrite, 0);
        AssertMethodPolicy<ServicesController>("CreateAsync", AppPolicies.ProductsAndServicesWrite, 1);
        AssertMethodPolicy<ServicesController>("EditAsync", AppPolicies.ProductsAndServicesWrite, 1);
        AssertMethodPolicy<ServicesController>("EditAsync", AppPolicies.ProductsAndServicesWrite, 2);
        AssertMethodPolicy<ServicesController>("ConnectionsAsync", AppPolicies.ProductsAndServicesWrite, 1);
        AssertMethodPolicy<ServicesController>("ConnectionsAsync", AppPolicies.ProductsAndServicesWrite, 2);
        AssertMethodPolicy<ServicesController>("DeleteAsync", AppPolicies.ProductsAndServicesWrite, 1);
        AssertMethodPolicy<ServicesController>("DeleteConfirmedAsync", AppPolicies.ProductsAndServicesWrite, 1);
        AssertMethodPolicy<ServicesController>("RestoreAsync", AppPolicies.AdminOnly, 0);
        AssertMethodPolicy<ServicesController>("RestoreDeletedAsync", AppPolicies.AdminOnly, 1);
        AssertMethodPolicy<ServicesController>("PermanentDeleteAsync", AppPolicies.AdminOnly, 1);
    }

    [Fact]
    public void ApplicationAndCapabilityWriteActionsRequireProductsAndServicesWritePolicy()
    {
        AssertMethodPolicy<ApplicationsController>("CreateAsync", AppPolicies.ProductsAndServicesWrite, 0);
        AssertMethodPolicy<ApplicationsController>("CreateAsync", AppPolicies.ProductsAndServicesWrite, 1);
        AssertMethodPolicy<ApplicationsController>("EditAsync", AppPolicies.ProductsAndServicesWrite, 1);
        AssertMethodPolicy<ApplicationsController>("EditAsync", AppPolicies.ProductsAndServicesWrite, 2);

        AssertMethodPolicy<CapabilitiesController>("CreateAsync", AppPolicies.ProductsAndServicesWrite, 1);
        AssertMethodPolicy<CapabilitiesController>("CreateAsync", AppPolicies.ProductsAndServicesWrite, 2);
        AssertMethodPolicy<CapabilitiesController>("EditAsync", AppPolicies.ProductsAndServicesWrite, 1);
        AssertMethodPolicy<CapabilitiesController>("EditAsync", AppPolicies.ProductsAndServicesWrite, 2);
        AssertMethodPolicy<CapabilitiesController>("DeleteAsync", AppPolicies.ProductsAndServicesWrite, 1);
        AssertMethodPolicy<CapabilitiesController>("DeleteConfirmedAsync", AppPolicies.ProductsAndServicesWrite, 1);
    }

    [Fact]
    public void BrmModelWriteActionsRequireProductsAndServicesWritePolicy()
    {
        AssertMethodPolicy<BrmModelsController>("Create", AppPolicies.ProductsAndServicesWrite, 0);
        AssertMethodPolicy<BrmModelsController>("CreateAsync", AppPolicies.ProductsAndServicesWrite, 1);
        AssertMethodPolicy<BrmModelsController>("EditAsync", AppPolicies.ProductsAndServicesWrite, 1);
        AssertMethodPolicy<BrmModelsController>("EditAsync", AppPolicies.ProductsAndServicesWrite, 2);
    }

    [Fact]
    public void ReferenceWriteActionsRequireAdminOnlyPolicy()
    {
        AssertMethodPolicy<ReferenceController>("VerifyImportAsync", AppPolicies.AdminOnly, 1);
        AssertMethodPolicy<ReferenceController>("ImportVerifiedAsync", AppPolicies.AdminOnly, 1);
        AssertMethodPolicy<ReferenceController>("RestoreAsync", AppPolicies.AdminOnly, 0);
        AssertMethodPolicy<ReferenceController>("RestoreArmAsync", AppPolicies.AdminOnly, 0);
        AssertMethodPolicy<ReferenceController>("RestoreBrmAsync", AppPolicies.AdminOnly, 0);
        AssertMethodPolicy<ReferenceController>("DeleteComponentAsync", AppPolicies.AdminOnly, 2);
        AssertMethodPolicy<ReferenceController>("RestoreComponentAsync", AppPolicies.AdminOnly, 2);
        AssertMethodPolicy<ReferenceController>("PermanentlyDeleteComponentAsync", AppPolicies.AdminOnly, 2);
        AssertMethodPolicy<ReportsController>("ExportMappingsCsvAsync", AppPolicies.AdminOnly, 0);
    }

    private static void AssertClassPolicy<TController>(string expectedPolicy)
    {
        var authorizeAttribute = typeof(TController).GetCustomAttribute<AuthorizeAttribute>();
        Assert.NotNull(authorizeAttribute);
        Assert.Equal(expectedPolicy, authorizeAttribute.Policy);
    }

    private static void AssertMethodPolicy<TController>(string methodName, string expectedPolicy, int parameterCount)
    {
        var method = typeof(TController).GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Single(candidate => candidate.Name == methodName && candidate.GetParameters().Length == parameterCount);

        var authorizeAttribute = method.GetCustomAttribute<AuthorizeAttribute>();
        Assert.NotNull(authorizeAttribute);
        Assert.Equal(expectedPolicy, authorizeAttribute.Policy);
    }
}