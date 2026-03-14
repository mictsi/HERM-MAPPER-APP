using HERMMapperApp.Configuration;
using HERMMapperApp.Data;
using HERMMapperApp.Models;
using HERMMapperApp.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Security.Claims;
using Xunit;

namespace HERMMapperApp.Tests;

public sealed class ProgramStartupTests
{
    [Fact]
    public void BuildDiagnosticsOptionsUsesConfiguredValuesAndParsesLogLevels()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Diagnostics:Console:Enabled"] = "false",
                ["Diagnostics:Console:LogLevel"] = "Warning",
                ["Diagnostics:Sql:Enabled"] = "true",
                ["Diagnostics:Sql:LogLevel"] = "Debug",
                ["Diagnostics:Sql:IncludeSensitiveData"] = "true",
                ["Diagnostics:Sql:EnableDetailedErrors"] = "true"
            })
            .Build();

        var options = Program.BuildDiagnosticsOptions(configuration);

        Assert.False(options.ConsoleLoggingEnabled);
        Assert.Equal(LogLevel.Warning, options.ConsoleLogLevel);
        Assert.True(options.SqlLoggingEnabled);
        Assert.Equal(LogLevel.Debug, options.SqlLogLevel);
        Assert.True(options.SqlSensitiveDataLoggingEnabled);
        Assert.True(options.SqlDetailedErrorsEnabled);
    }

    [Fact]
    public void BuildAuthenticationSecurityOptionsUsesConfiguredValues()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Security:Authentication:SessionTimeoutMinutes"] = "60",
                ["Security:Authentication:MaxFailedLoginAttempts"] = "15",
                ["Security:Authentication:LockoutMinutes"] = "1"
            })
            .Build();

        var options = Program.BuildAuthenticationSecurityOptions(configuration);

        Assert.Equal(60, options.SessionTimeoutMinutes);
        Assert.Equal(15, options.MaxFailedLoginAttempts);
        Assert.Equal(1, options.LockoutMinutes);
        Assert.Equal(TimeSpan.FromMinutes(60), options.SessionTimeout);
    }

    [Fact]
    public void BuildLocalAuthenticationOptionsUsesConfiguredValue()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Security:Authentication:Local:Enabled"] = "false"
            })
            .Build();

        var options = Program.BuildLocalAuthenticationOptions(configuration);

        Assert.False(options.Enabled);
    }

    [Fact]
    public void BuildOpenIdConnectAuthenticationOptionsUsesConfiguredRoleMappings()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Security:Authentication:OpenIdConnect:Enabled"] = "true",
                ["Security:Authentication:OpenIdConnect:EmitTokensAndClaimsToConsole"] = "true",
                ["Security:Authentication:OpenIdConnect:Authority"] = "https://login.example.com",
                ["Security:Authentication:OpenIdConnect:ClientId"] = "client-id",
                ["Security:Authentication:OpenIdConnect:RoleGroupMappings:Administrator:0"] = "11111111-1111-1111-1111-111111111111",
                ["Security:Authentication:OpenIdConnect:RoleGroupMappings:Contributor:0"] = "22222222-2222-2222-2222-222222222222"
            })
            .Build();

        var options = Program.BuildOpenIdConnectAuthenticationOptions(configuration);

        Assert.True(options.Enabled);
    Assert.True(options.EmitTokensAndClaimsToConsole);
        Assert.Equal("https://login.example.com", options.Authority);
        Assert.Equal("client-id", options.ClientId);
        Assert.Equal("11111111-1111-1111-1111-111111111111", options.RoleGroupMappings[AppRoles.Administrator].Single());
        Assert.Equal("22222222-2222-2222-2222-222222222222", options.RoleGroupMappings[AppRoles.Contributor].Single());
    }

    [Fact]
    public void BuildCookieSecurePolicyUsesEnvironmentDefaultAndAllowsOverride()
    {
        var configuration = new ConfigurationBuilder().Build();
        var overrideConfiguration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Security:Authentication:RequireHttpsCookies"] = "true"
            })
            .Build();

        Assert.Equal(CookieSecurePolicy.SameAsRequest, Program.BuildCookieSecurePolicy(configuration, "Development"));
        Assert.Equal(CookieSecurePolicy.Always, Program.BuildCookieSecurePolicy(configuration, "Production"));
        Assert.Equal(CookieSecurePolicy.Always, Program.BuildCookieSecurePolicy(overrideConfiguration, "Development"));
    }

    [Fact]
    public void BuildCookieNameUsesHostPrefixOnlyWhenCookiesRequireHttps()
    {
        Assert.Equal("HERM.Mapper.Antiforgery", Program.BuildCookieName("HERM.Mapper.Antiforgery", CookieSecurePolicy.SameAsRequest));
        Assert.Equal("__Host-HERM.Mapper.Antiforgery", Program.BuildCookieName("HERM.Mapper.Antiforgery", CookieSecurePolicy.Always));
    }

    [Fact]
    public void BuildAuthenticationCookieSameSiteUsesLaxForOpenIdConnect()
    {
        Assert.Equal(SameSiteMode.Strict, Program.BuildAuthenticationCookieSameSite(openIdConnectEnabled: false));
        Assert.Equal(SameSiteMode.Lax, Program.BuildAuthenticationCookieSameSite(openIdConnectEnabled: true));
    }

    [Fact]
    public void ParseLogLevelReturnsFallbackWhenValueIsInvalid()
    {
        var parsed = Program.ParseLogLevel("not-a-level", LogLevel.Error);

        Assert.Equal(LogLevel.Error, parsed);
    }

    [Fact]
    public async Task ConfigureApplicationServicesRegistersSqliteServicesAndInitializeDatabaseAsyncSeedsDefaults()
    {
        using var contentRoot = new TemporaryDirectory();
        Directory.CreateDirectory(Path.Combine(contentRoot.Path, "App_Data"));
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["HermWorkbook:AutoImportOnFirstRun"] = "false",
                ["SampleRelationships:AutoImportOnFirstRun"] = "false"
            })
            .Build();
        var databaseConfiguration = new ResolvedDatabaseConfiguration(
            DatabaseProviderKind.Sqlite,
            $"Data Source={Path.Combine(contentRoot.Path, "App_Data", "startup-tests.db")}");
        var diagnosticsOptions = new StartupDiagnosticsOptions(
            ConsoleLoggingEnabled: false,
            ConsoleLogLevel: LogLevel.Information,
            SqlLoggingEnabled: false,
            SqlLogLevel: LogLevel.Information,
            SqlSensitiveDataLoggingEnabled: false,
            SqlDetailedErrorsEnabled: false);

        var services = new ServiceCollection();
        services.AddLogging();

        Program.ConfigureApplicationServices(services, configuration, "Development", databaseConfiguration, diagnosticsOptions);

        await using var provider = services.BuildServiceProvider();
        await Program.InitializeDatabaseAsync(provider);

        await using var scope = provider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var configurableFieldService = scope.ServiceProvider.GetRequiredService<ConfigurableFieldService>();

        Assert.Equal("Microsoft.EntityFrameworkCore.Sqlite", dbContext.Database.ProviderName);
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<AuditLogService>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<AppSettingsService>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<AppAuthenticationService>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<ComponentVersioningService>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<ConfiguredTimeZoneService>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<DatabaseInitializer>());

        var lifecycleStatuses = await configurableFieldService.GetOptionsAsync(ConfigurableFieldNames.LifecycleStatus);
        Assert.Equal(
            ConfigurableFieldNames.GetDefaultValues(ConfigurableFieldNames.LifecycleStatus),
            lifecycleStatuses.Select(x => x.Value).ToList());

        var displayTimeZone = await dbContext.AppSettings
            .AsNoTracking()
            .SingleAsync(x => x.Key == AppSettingKeys.DisplayTimeZone);

        var bootstrapAdmin = await dbContext.AppUsers
            .AsNoTracking()
            .SingleAsync();

        Assert.Equal(AppSettingDefaults.DisplayTimeZone, displayTimeZone.Value);
        Assert.Equal("admin", bootstrapAdmin.UserName);
        Assert.Equal(AppRoles.Administrator, bootstrapAdmin.RoleName);
    }

    [Fact]
    public async Task ConfigureApplicationServicesRegistersRolePoliciesForRequestedAccessMatrix()
    {
        using var contentRoot = new TemporaryDirectory();
        Directory.CreateDirectory(Path.Combine(contentRoot.Path, "App_Data"));
        var configuration = new ConfigurationBuilder().Build();
        var databaseConfiguration = new ResolvedDatabaseConfiguration(
            DatabaseProviderKind.Sqlite,
            $"Data Source={Path.Combine(contentRoot.Path, "App_Data", "auth-tests.db")}");
        var diagnosticsOptions = new StartupDiagnosticsOptions(
            ConsoleLoggingEnabled: false,
            ConsoleLogLevel: LogLevel.Information,
            SqlLoggingEnabled: false,
            SqlLogLevel: LogLevel.Information,
            SqlSensitiveDataLoggingEnabled: false,
            SqlDetailedErrorsEnabled: false);

        var services = new ServiceCollection();
        services.AddLogging();

        Program.ConfigureApplicationServices(services, configuration, "Development", databaseConfiguration, diagnosticsOptions);

        await using var provider = services.BuildServiceProvider();
        var authorizationService = provider.GetRequiredService<IAuthorizationService>();

        var viewer = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.Name, "viewer"),
            new Claim(ClaimTypes.Role, AppRoles.Viewer)
        ], "test"));
        var contributor = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.Name, "contributor"),
            new Claim(ClaimTypes.Role, AppRoles.Contributor)
        ], "test"));
        var legacyAdmin = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.Name, "admin"),
            new Claim(ClaimTypes.Role, "Admin")
        ], "test"));

        Assert.True((await authorizationService.AuthorizeAsync(viewer, null, AppPolicies.CatalogueRead)).Succeeded);
        Assert.False((await authorizationService.AuthorizeAsync(viewer, null, AppPolicies.ProductsAndServicesWrite)).Succeeded);
        Assert.True((await authorizationService.AuthorizeAsync(contributor, null, AppPolicies.ProductsAndServicesWrite)).Succeeded);
        Assert.True((await authorizationService.AuthorizeAsync(legacyAdmin, null, AppPolicies.AdminOnly)).Succeeded);
    }


    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"herm-mapper-program-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
