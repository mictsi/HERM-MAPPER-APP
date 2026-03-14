using HERMMapperApp.Configuration;
using HERMMapperApp.Data;
using HERMMapperApp.Models;
using HERMMapperApp.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using System.Reflection;
using System.Security.Claims;
using System.Text.Encodings.Web;
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
    public void BuildAuthenticationSecurityOptionsClampsConfiguredMinimums()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Security:Authentication:SessionTimeoutMinutes"] = "0",
                ["Security:Authentication:MaxFailedLoginAttempts"] = "-2",
                ["Security:Authentication:LockoutMinutes"] = "0"
            })
            .Build();

        var options = Program.BuildAuthenticationSecurityOptions(configuration);

        Assert.Equal(1, options.SessionTimeoutMinutes);
        Assert.Equal(1, options.MaxFailedLoginAttempts);
        Assert.Equal(1, options.LockoutMinutes);
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
    public void BuildOpenIdConnectAuthenticationOptionsReturnsDisabledDefaultsAndNormalizesInputs()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Security:Authentication:OpenIdConnect:Enabled"] = "false",
                ["Security:Authentication:OpenIdConnect:Scopes:0"] = "openid",
                ["Security:Authentication:OpenIdConnect:Scopes:1"] = " profile ",
                ["Security:Authentication:OpenIdConnect:Scopes:2"] = "profile",
                ["Security:Authentication:OpenIdConnect:RoleGroupMappings:Viewer:0"] = " group-a ",
                ["Security:Authentication:OpenIdConnect:RoleGroupMappings:Viewer:1"] = "group-a",
                ["Security:Authentication:OpenIdConnect:RoleGroupMappings:Viewer:2"] = ""
            })
            .Build();

        var options = Program.BuildOpenIdConnectAuthenticationOptions(configuration);

        Assert.False(options.Enabled);
        Assert.Equal("OpenID Connect", options.DisplayName);
        Assert.Equal(["openid", "profile"], options.Scopes);
        Assert.Equal(["group-a"], options.RoleGroupMappings[AppRoles.Viewer]);
        Assert.Equal("/signin-oidc", options.CallbackPath);
        Assert.Equal("/signout-callback-oidc", options.SignedOutCallbackPath);
    }

    [Fact]
    public void BuildOpenIdConnectAuthenticationOptionsThrowsWhenRoleMappingUnsupported()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Security:Authentication:OpenIdConnect:Enabled"] = "false",
                ["Security:Authentication:OpenIdConnect:RoleGroupMappings:SuperAdmin:0"] = "group-1"
            })
            .Build();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            Program.BuildOpenIdConnectAuthenticationOptions(configuration));

        Assert.Contains("unsupported role", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildOpenIdConnectAuthenticationOptionsThrowsWhenClientIdMissing()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Security:Authentication:OpenIdConnect:Enabled"] = "true",
                ["Security:Authentication:OpenIdConnect:Authority"] = "https://login.example.com"
            })
            .Build();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            Program.BuildOpenIdConnectAuthenticationOptions(configuration));

        Assert.Contains("ClientId", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildOpenIdConnectAuthenticationOptionsThrowsWhenAuthorityAndMetadataMissing()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Security:Authentication:OpenIdConnect:Enabled"] = "true",
                ["Security:Authentication:OpenIdConnect:ClientId"] = "client-id"
            })
            .Build();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            Program.BuildOpenIdConnectAuthenticationOptions(configuration));

        Assert.Contains("Authority or MetadataAddress", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildAuthenticationFailureRedirectUriIncludesOnlyLocalReturnUrl()
    {
        var redirectUri = Program.BuildAuthenticationFailureRedirectUri("/Products?status=Draft", "OpenID Connect sign-in failed.");

        Assert.Equal("/Account/Login?error=OpenID%20Connect%20sign-in%20failed.&returnUrl=%2FProducts%3Fstatus%3DDraft", redirectUri);
    }

    [Fact]
    public void BuildAuthenticationFailureRedirectUriExcludesExternalReturnUrl()
    {
        var redirectUri = Program.BuildAuthenticationFailureRedirectUri("https://contoso.example/products", "OpenID Connect sign-in failed.");

        Assert.Equal("/Account/Login?error=OpenID%20Connect%20sign-in%20failed.", redirectUri);
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
        Assert.Equal(CookieSecurePolicy.SameAsRequest, Program.BuildCookieSecurePolicy(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Security:Authentication:RequireHttpsCookies"] = "false"
        }).Build(), "Production"));
    }

    [Fact]
    public void BuildCookieNameUsesHostPrefixOnlyWhenCookiesRequireHttps()
    {
        Assert.Equal("HERM.Mapper.Antiforgery", Program.BuildCookieName("HERM.Mapper.Antiforgery", CookieSecurePolicy.SameAsRequest));
        Assert.Equal("__Host-HERM.Mapper.Antiforgery", Program.BuildCookieName("HERM.Mapper.Antiforgery", CookieSecurePolicy.Always));
    }

    [Fact]
    public void ProgramPrivateConstructorCanBeInvokedViaReflection()
    {
        var constructor = typeof(Program).GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, binder: null, Type.EmptyTypes, modifiers: null);

        Assert.NotNull(constructor);
        Assert.IsType<Program>(constructor!.Invoke(null));
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
    public void ConfigureLoggingRegistersSimpleConsoleOptions()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().Build();

        services.AddLogging(logging =>
            Program.ConfigureLogging(
                logging,
                configuration,
                new StartupDiagnosticsOptions(true, LogLevel.Warning, true, LogLevel.Error, false, false)));

        using var provider = services.BuildServiceProvider();
        var loggerFactory = provider.GetRequiredService<ILoggerFactory>();
        var logger = loggerFactory.CreateLogger("startup-test");
        var consoleOptions = provider.GetRequiredService<IOptionsMonitor<SimpleConsoleFormatterOptions>>().Get(ConsoleFormatterNames.Simple);

        Assert.NotNull(logger);
        Assert.NotNull(consoleOptions);
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

    [Fact]
    public void ConfigureApplicationServicesConfiguresAntiforgeryAndSqliteDiagnostics()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        Program.ConfigureApplicationServices(
            services,
            new ConfigurationBuilder().Build(),
            "Development",
            new ResolvedDatabaseConfiguration(DatabaseProviderKind.Sqlite, "Data Source=:memory:"),
            new StartupDiagnosticsOptions(false, LogLevel.Information, true, LogLevel.Debug, true, true));

        using var provider = services.BuildServiceProvider();
        var antiforgeryOptions = provider.GetRequiredService<IOptions<AntiforgeryOptions>>().Value;
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        Assert.Equal("HERM.Mapper.Antiforgery", antiforgeryOptions.Cookie.Name);
        Assert.True(antiforgeryOptions.Cookie.HttpOnly);
        Assert.Equal("/", antiforgeryOptions.Cookie.Path);
        Assert.Equal(CookieSecurePolicy.SameAsRequest, antiforgeryOptions.Cookie.SecurePolicy);
        Assert.Equal(SameSiteMode.Strict, antiforgeryOptions.Cookie.SameSite);
        Assert.Equal("Microsoft.EntityFrameworkCore.Sqlite", dbContext.Database.ProviderName);
    }

    [Fact]
    public void ConfigureApplicationServicesSupportsSqlServerRegistration()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        Program.ConfigureApplicationServices(
            services,
            new ConfigurationBuilder().Build(),
            "Production",
            new ResolvedDatabaseConfiguration(DatabaseProviderKind.SqlServer, "Server=(localdb)\\mssqllocaldb;Database=HERMMapperAppTests;Trusted_Connection=True;TrustServerCertificate=True"),
            new StartupDiagnosticsOptions(false, LogLevel.Information, false, LogLevel.Information, false, false));

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        Assert.Equal("Microsoft.EntityFrameworkCore.SqlServer", dbContext.Database.ProviderName);
    }

    [Fact]
    public void ConfigureApplicationServicesThrowsWhenAllAuthenticationMethodsAreDisabled()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Security:Authentication:Local:Enabled"] = "false",
                ["Security:Authentication:OpenIdConnect:Enabled"] = "false"
            })
            .Build();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            Program.ConfigureApplicationServices(
                new ServiceCollection(),
                configuration,
                "Development",
                new ResolvedDatabaseConfiguration(DatabaseProviderKind.Sqlite, "Data Source=:memory:"),
                new StartupDiagnosticsOptions(false, LogLevel.Information, false, LogLevel.Information, false, false)));

        Assert.Contains("At least one authentication method must be enabled", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ConfigureApplicationServicesConfiguresOpenIdConnectEventsAndCookieSettings()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Security:Authentication:Local:Enabled"] = "true",
                ["Security:Authentication:OpenIdConnect:Enabled"] = "true",
                ["Security:Authentication:OpenIdConnect:Authority"] = "https://login.example.com",
                ["Security:Authentication:OpenIdConnect:ClientId"] = "client-id",
                ["Security:Authentication:OpenIdConnect:ClientSecret"] = "client-secret",
                ["Security:Authentication:OpenIdConnect:DisplayName"] = "Contoso Entra",
                ["Security:Authentication:OpenIdConnect:Scopes:0"] = "openid",
                ["Security:Authentication:OpenIdConnect:Scopes:1"] = "profile",
                ["Security:Authentication:OpenIdConnect:RoleGroupMappings:Viewer:0"] = "group-viewer"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();

        Program.ConfigureApplicationServices(
            services,
            configuration,
            "Production",
            new ResolvedDatabaseConfiguration(DatabaseProviderKind.Sqlite, "Data Source=:memory:"),
            new StartupDiagnosticsOptions(false, LogLevel.Information, false, LogLevel.Information, false, false));

        using var provider = services.BuildServiceProvider();
        var cookieOptions = provider.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(CookieAuthenticationDefaults.AuthenticationScheme);
        var oidcOptions = provider.GetRequiredService<IOptionsMonitor<OpenIdConnectOptions>>().Get(OpenIdConnectDefaults.AuthenticationScheme);

        Assert.Equal("__Host-HERM.Mapper.Auth", cookieOptions.Cookie.Name);
        Assert.Equal(SameSiteMode.Lax, cookieOptions.Cookie.SameSite);
        Assert.Equal("client-id", oidcOptions.ClientId);
        Assert.Equal("client-secret", oidcOptions.ClientSecret);
        Assert.Equal("https://login.example.com", oidcOptions.Authority);
        Assert.Equal(["openid", "profile"], oidcOptions.Scope.ToArray());
        Assert.NotNull(oidcOptions.Events.OnTokenValidated);
        Assert.NotNull(oidcOptions.Events.OnAuthenticationFailed);
        Assert.NotNull(oidcOptions.Events.OnRemoteFailure);
        Assert.NotNull(oidcOptions.Events.OnRedirectToIdentityProviderForSignOut);
    }

    [Fact]
    public void ConfigureApplicationServicesUsesMetadataAddressWhenProvided()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Security:Authentication:Local:Enabled"] = "true",
                ["Security:Authentication:OpenIdConnect:Enabled"] = "true",
                ["Security:Authentication:OpenIdConnect:MetadataAddress"] = "https://login.example.com/.well-known/openid-configuration",
                ["Security:Authentication:OpenIdConnect:ClientId"] = "client-id"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();

        Program.ConfigureApplicationServices(
            services,
            configuration,
            "Production",
            new ResolvedDatabaseConfiguration(DatabaseProviderKind.Sqlite, "Data Source=:memory:"),
            new StartupDiagnosticsOptions(false, LogLevel.Information, false, LogLevel.Information, false, false));

        using var provider = services.BuildServiceProvider();
        var oidcOptions = provider.GetRequiredService<IOptionsMonitor<OpenIdConnectOptions>>().Get(OpenIdConnectDefaults.AuthenticationScheme);

        Assert.Equal("https://login.example.com/.well-known/openid-configuration", oidcOptions.MetadataAddress);
        Assert.True(string.IsNullOrWhiteSpace(oidcOptions.Authority));
    }

    [Fact]
    public async Task OpenIdConnectOnTokenValidatedRedirectsWhenMappedRolesMissing()
    {
        using var eventFixture = CreateTokenValidatedContext(
            emitDebugDetails: false,
            principal: new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim("sub", "oidc-user"),
                new Claim("name", "OIDC User"),
                new Claim("groups", "group-none")
            ], "oidc")),
            options: new HERMMapperApp.Configuration.OpenIdConnectAuthenticationOptions
            {
                Enabled = true,
                Authority = "https://login.example.com",
                ClientId = "client-id",
                RoleGroupMappings = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
                {
                    [AppRoles.Viewer] = ["group-viewer"]
                }
            });
        var eventContext = eventFixture.Context;

        await eventContext.Options.Events.OnTokenValidated(eventContext);

        Assert.Equal("/Account/Login?error=Your%20account%20does%20not%20map%20to%20any%20configured%20application%20role.&returnUrl=%2FReports", eventContext.Response.Headers.Location.ToString());
    }

    [Fact]
    public async Task OpenIdConnectOnTokenValidatedReplacesPrincipalWhenClaimsAreValid()
    {
        using var eventFixture = CreateTokenValidatedContext(
            emitDebugDetails: false,
            principal: new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim("sub", "oidc-user"),
                new Claim("name", "OIDC User"),
                new Claim("groups", "group-viewer")
            ], "oidc")),
            options: new HERMMapperApp.Configuration.OpenIdConnectAuthenticationOptions
            {
                Enabled = true,
                Authority = "https://login.example.com",
                ClientId = "client-id",
                RoleGroupMappings = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
                {
                    [AppRoles.Viewer] = ["group-viewer"]
                }
            });
        var eventContext = eventFixture.Context;

        await eventContext.Options.Events.OnTokenValidated(eventContext);

        Assert.NotNull(eventContext.Principal);
        Assert.True(eventContext.Principal!.IsInRole(AppRoles.Viewer));
        Assert.True(AppAuthenticationService.IsOpenIdConnectUser(eventContext.Principal));
        Assert.False(eventContext.Response.Headers.ContainsKey("Location"));
    }

    [Fact]
    public async Task OpenIdConnectFailureEventsRedirectToLogin()
    {
        using var eventFixture = CreateTokenValidatedContext(
            emitDebugDetails: false,
            principal: new ClaimsPrincipal(new ClaimsIdentity()),
            options: new HERMMapperApp.Configuration.OpenIdConnectAuthenticationOptions
            {
                Enabled = true,
                Authority = "https://login.example.com",
                ClientId = "client-id",
                RoleGroupMappings = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
                {
                    [AppRoles.Viewer] = ["group-viewer"]
                }
            });
        var eventContext = eventFixture.Context;

        var failedContext = new AuthenticationFailedContext(eventContext.HttpContext, eventContext.Scheme, eventContext.Options)
        {
            Properties = new AuthenticationProperties { RedirectUri = "/Products" }
        };
        await eventContext.Options.Events.OnAuthenticationFailed(failedContext);

        var remoteFailureContext = new RemoteFailureContext(eventContext.HttpContext, eventContext.Scheme, eventContext.Options, new InvalidOperationException("remote failure"))
        {
            Properties = new AuthenticationProperties { RedirectUri = "/Products" }
        };
        await eventContext.Options.Events.OnRemoteFailure(remoteFailureContext);

        Assert.Equal("/Account/Login?error=OpenID%20Connect%20sign-in%20failed.&returnUrl=%2FProducts", failedContext.Response.Headers.Location.ToString());
        Assert.Equal("/Account/Login?error=OpenID%20Connect%20sign-in%20failed.&returnUrl=%2FProducts", remoteFailureContext.Response.Headers.Location.ToString());
    }

    [Fact]
    public async Task OpenIdConnectSignOutRedirectAddsIdTokenHint()
    {
        using var eventFixture = CreateTokenValidatedContext(
            emitDebugDetails: false,
            principal: new ClaimsPrincipal(new ClaimsIdentity()),
            options: new HERMMapperApp.Configuration.OpenIdConnectAuthenticationOptions
            {
                Enabled = true,
                Authority = "https://login.example.com",
                ClientId = "client-id",
                RoleGroupMappings = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
                {
                    [AppRoles.Viewer] = ["group-viewer"]
                }
            });

        var properties = new AuthenticationProperties();
        properties.StoreTokens([new AuthenticationToken { Name = "id_token", Value = "token-123" }]);
        var redirectContext = new RedirectContext(eventFixture.Context.HttpContext, eventFixture.Context.Scheme, eventFixture.Context.Options, properties)
        {
            ProtocolMessage = new OpenIdConnectMessage()
        };

        await eventFixture.Context.Options.Events.OnRedirectToIdentityProviderForSignOut(redirectContext);

        Assert.Equal("token-123", redirectContext.ProtocolMessage.IdTokenHint);
    }

    [Fact]
    public void ConfigurePipelineExecutesProductionBranch()
    {
        var contentRootPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../src/HERM-MAPPER-APP"));
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ApplicationName = typeof(Program).Assembly.FullName,
            ContentRootPath = contentRootPath,
            EnvironmentName = "Production"
        });

        builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie();
        builder.Services.AddAuthorization();

        using var app = builder.Build();

        var exception = Assert.Throws<InvalidOperationException>(() => Program.ConfigurePipeline(app));

        Assert.Equal("Production", app.Environment.EnvironmentName);
        Assert.Contains("static resources manifest file", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LogOpenIdConnectDebugDetailsLogsClaimsAndTokensWhenEnabled()
    {
        using var loggerProvider = new CaptureLoggerProvider();
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(loggerProvider));
        using var services = new ServiceCollection()
            .AddSingleton<ILoggerFactory>(loggerFactory)
            .BuildServiceProvider();
        var httpContext = new DefaultHttpContext { RequestServices = services };
        var scheme = new AuthenticationScheme(OpenIdConnectDefaults.AuthenticationScheme, null, typeof(TestAuthHandler));
        var options = new OpenIdConnectOptions();
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.Name, "Ada"),
            new Claim(ClaimTypes.Email, "ada@example.com")
        ], "oidc"));
        var properties = new AuthenticationProperties();
        properties.StoreTokens([new AuthenticationToken { Name = "id_token", Value = "abc123" }]);
        var context = new TokenValidatedContext(httpContext, scheme, options, principal, properties)
        {
            Principal = principal,
            Properties = properties,
            SecurityToken = new System.IdentityModel.Tokens.Jwt.JwtSecurityToken()
        };

        Program.LogOpenIdConnectDebugDetails(context, new HERMMapperApp.Configuration.OpenIdConnectAuthenticationOptions
        {
            Enabled = true,
            EmitTokensAndClaimsToConsole = true,
            Authority = "https://login.example.com",
            ClientId = "client-id"
        });

        Assert.Contains(loggerProvider.Messages, message => message.Contains("OpenID Connect token validation debug output", StringComparison.Ordinal));
        Assert.Contains(loggerProvider.Messages, message => message.Contains("ada@example.com", StringComparison.Ordinal));
        Assert.Contains(loggerProvider.Messages, message => message.Contains("id_token = abc123", StringComparison.Ordinal));
    }

    [Fact]
    public void LogOpenIdConnectDebugDetailsReturnsWithoutLoggerFactory()
    {
        using var provider = new ServiceCollection().BuildServiceProvider();
        var httpContext = new DefaultHttpContext
        {
            RequestServices = provider
        };
        var scheme = new AuthenticationScheme(OpenIdConnectDefaults.AuthenticationScheme, null, typeof(TestAuthHandler));
        var context = new TokenValidatedContext(httpContext, scheme, new OpenIdConnectOptions(), new ClaimsPrincipal(new ClaimsIdentity()), new AuthenticationProperties())
        {
            Principal = new ClaimsPrincipal(new ClaimsIdentity())
        };

        Program.LogOpenIdConnectDebugDetails(context, new HERMMapperApp.Configuration.OpenIdConnectAuthenticationOptions
        {
            Enabled = true,
            EmitTokensAndClaimsToConsole = true,
            Authority = "https://login.example.com",
            ClientId = "client-id"
        });

        Assert.Empty(httpContext.Response.Headers);
    }

    [Fact]
    public void LogOpenIdConnectDebugDetailsLogsNoneWhenClaimsAndTokensMissing()
    {
        using var loggerProvider = new CaptureLoggerProvider();
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(loggerProvider));
        using var services = new ServiceCollection()
            .AddSingleton<ILoggerFactory>(loggerFactory)
            .BuildServiceProvider();
        var httpContext = new DefaultHttpContext { RequestServices = services };
        var scheme = new AuthenticationScheme(OpenIdConnectDefaults.AuthenticationScheme, null, typeof(TestAuthHandler));
        var context = new TokenValidatedContext(httpContext, scheme, new OpenIdConnectOptions(), new ClaimsPrincipal(new ClaimsIdentity()), new AuthenticationProperties())
        {
            Principal = new ClaimsPrincipal(new ClaimsIdentity()),
            Properties = new AuthenticationProperties()
        };

        Program.LogOpenIdConnectDebugDetails(context, new HERMMapperApp.Configuration.OpenIdConnectAuthenticationOptions
        {
            Enabled = true,
            EmitTokensAndClaimsToConsole = true,
            Authority = "https://login.example.com",
            ClientId = "client-id"
        });

        Assert.Contains(loggerProvider.Messages, message => message.Contains("Claims:", StringComparison.Ordinal));
        Assert.Contains(loggerProvider.Messages, message => message.Contains("  <none>", StringComparison.Ordinal));
        Assert.Contains(loggerProvider.Messages, message => message.Contains("Tokens:", StringComparison.Ordinal));
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

    private static OidcEventTestContext CreateTokenValidatedContext(
        bool emitDebugDetails,
        ClaimsPrincipal principal,
        HERMMapperApp.Configuration.OpenIdConnectAuthenticationOptions options) =>
        OidcEventTestContext.Create(emitDebugDetails, principal, options);

    private sealed class OidcEventTestContext : IDisposable
    {
        private readonly Action dispose;

        public OidcEventTestContext(Action dispose, TokenValidatedContext context)
        {
            this.dispose = dispose;
            Context = context;
        }

        public TokenValidatedContext Context { get; }

        public static OidcEventTestContext Create(
            bool emitDebugDetails,
            ClaimsPrincipal principal,
            HERMMapperApp.Configuration.OpenIdConnectAuthenticationOptions options)
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Security:Authentication:Local:Enabled"] = "true",
                    ["Security:Authentication:OpenIdConnect:Enabled"] = "true",
                    ["Security:Authentication:OpenIdConnect:Authority"] = options.Authority,
                    ["Security:Authentication:OpenIdConnect:ClientId"] = options.ClientId,
                    ["Security:Authentication:OpenIdConnect:EmitTokensAndClaimsToConsole"] = emitDebugDetails.ToString(),
                    ["Security:Authentication:OpenIdConnect:NameClaimType"] = options.NameClaimType,
                    ["Security:Authentication:OpenIdConnect:EmailClaimType"] = options.EmailClaimType,
                    ["Security:Authentication:OpenIdConnect:GivenNameClaimType"] = options.GivenNameClaimType,
                    ["Security:Authentication:OpenIdConnect:SurnameClaimType"] = options.SurnameClaimType,
                    ["Security:Authentication:OpenIdConnect:GroupClaimType"] = options.GroupClaimType,
                    ["Security:Authentication:OpenIdConnect:SubjectClaimType"] = options.SubjectClaimType,
                }.Concat(options.RoleGroupMappings.SelectMany(pair => pair.Value.Select((value, index) =>
                    new KeyValuePair<string, string?>("Security:Authentication:OpenIdConnect:RoleGroupMappings:" + pair.Key + ":" + index, value)))) )
                .Build();

            var services = new ServiceCollection();
            services.AddLogging();
            Program.ConfigureApplicationServices(
                services,
                configuration,
                "Development",
                new ResolvedDatabaseConfiguration(DatabaseProviderKind.Sqlite, "Data Source=:memory:"),
                new StartupDiagnosticsOptions(false, LogLevel.Information, false, LogLevel.Information, false, false));

            var serviceProvider = services.BuildServiceProvider();
            var httpContext = new DefaultHttpContext { RequestServices = serviceProvider };
            var scheme = new AuthenticationScheme(OpenIdConnectDefaults.AuthenticationScheme, null, typeof(TestAuthHandler));
            var oidcOptions = serviceProvider.GetRequiredService<IOptionsMonitor<OpenIdConnectOptions>>().Get(OpenIdConnectDefaults.AuthenticationScheme);
            var properties = new AuthenticationProperties { RedirectUri = "/Reports" };

            return new OidcEventTestContext(
                serviceProvider.Dispose,
                new TokenValidatedContext(httpContext, scheme, oidcOptions, principal, properties)
                {
                    Principal = principal,
                    Properties = properties,
                    SecurityToken = new System.IdentityModel.Tokens.Jwt.JwtSecurityToken()
                });
        }

        public void Dispose()
        {
            dispose();
        }
    }

    private sealed class CaptureLoggerProvider : ILoggerProvider
    {
        public List<string> Messages { get; } = [];

        public ILogger CreateLogger(string categoryName) => new CaptureLogger(Messages);

        public void Dispose()
        {
        }
    }

    private sealed class CaptureLogger(List<string> messages) : ILogger
    {
        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            messages.Add(formatter(state, exception));
        }
    }

    private sealed class NullScope : IDisposable
    {
        public static NullScope Instance { get; } = new();

        public void Dispose()
        {
        }
    }

    private sealed class TestAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync() => Task.FromResult(AuthenticateResult.NoResult());
    }
}
