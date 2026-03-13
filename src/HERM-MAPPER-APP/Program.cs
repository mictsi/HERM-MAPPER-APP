using HERM_MAPPER_APP.Configuration;
using HERM_MAPPER_APP.Data;
using HERM_MAPPER_APP.Models;
using HERM_MAPPER_APP.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddEnvironmentVariables(prefix: "HERM_");

var databaseConfiguration = Program.ResolveDatabaseConfiguration(builder.Configuration, builder.Environment.ContentRootPath);
var diagnosticsOptions = Program.BuildDiagnosticsOptions(builder.Configuration);

Program.ConfigureLogging(builder.Logging, builder.Configuration, diagnosticsOptions);
Program.ConfigureApplicationServices(builder.Services, builder.Configuration, builder.Environment.EnvironmentName, databaseConfiguration, diagnosticsOptions);

var app = builder.Build();
await Program.InitializeDatabaseAsync(app.Services);
Program.ConfigurePipeline(app);
app.Run();

public sealed record StartupDiagnosticsOptions(
    bool ConsoleLoggingEnabled,
    LogLevel ConsoleLogLevel,
    bool SqlLoggingEnabled,
    LogLevel SqlLogLevel,
    bool SqlSensitiveDataLoggingEnabled,
    bool SqlDetailedErrorsEnabled);

public sealed record AuthenticationSecurityOptions(
    int SessionTimeoutMinutes,
    int MaxFailedLoginAttempts,
    int LockoutMinutes)
{
    public TimeSpan SessionTimeout => TimeSpan.FromMinutes(SessionTimeoutMinutes);

    public TimeSpan LockoutDuration => TimeSpan.FromMinutes(LockoutMinutes);
}

public partial class Program
{
    private const string AntiforgeryCookieName = "HERM.Mapper.Antiforgery";
    private const string AuthenticationCookieName = "HERM.Mapper.Auth";

    public static ResolvedDatabaseConfiguration ResolveDatabaseConfiguration(IConfiguration configuration, string contentRootPath) =>
        AppDatabaseConfiguration.Resolve(configuration, contentRootPath);

    public static StartupDiagnosticsOptions BuildDiagnosticsOptions(IConfiguration configuration) =>
        new(
            ConsoleLoggingEnabled: configuration.GetValue<bool?>("Diagnostics:Console:Enabled") ?? true,
            ConsoleLogLevel: ParseLogLevel(configuration["Diagnostics:Console:LogLevel"], LogLevel.Information),
            SqlLoggingEnabled: configuration.GetValue<bool?>("Diagnostics:Sql:Enabled") ?? false,
            SqlLogLevel: ParseLogLevel(configuration["Diagnostics:Sql:LogLevel"], LogLevel.Information),
            SqlSensitiveDataLoggingEnabled: configuration.GetValue<bool?>("Diagnostics:Sql:IncludeSensitiveData") ?? false,
            SqlDetailedErrorsEnabled: configuration.GetValue<bool?>("Diagnostics:Sql:EnableDetailedErrors") ?? false);

    public static AuthenticationSecurityOptions BuildAuthenticationSecurityOptions(IConfiguration configuration) =>
        new(
            SessionTimeoutMinutes: Math.Max(1, configuration.GetValue<int?>("Security:Authentication:SessionTimeoutMinutes") ?? 60),
            MaxFailedLoginAttempts: Math.Max(1, configuration.GetValue<int?>("Security:Authentication:MaxFailedLoginAttempts") ?? 15),
            LockoutMinutes: Math.Max(1, configuration.GetValue<int?>("Security:Authentication:LockoutMinutes") ?? 1));

    public static CookieSecurePolicy BuildCookieSecurePolicy(IConfiguration configuration, string environmentName)
    {
        var requireHttpsCookies = configuration.GetValue<bool?>("Security:Authentication:RequireHttpsCookies");
        if (requireHttpsCookies.HasValue)
        {
            return requireHttpsCookies.Value ? CookieSecurePolicy.Always : CookieSecurePolicy.SameAsRequest;
        }

        return string.Equals(environmentName, "Development", StringComparison.OrdinalIgnoreCase)
            ? CookieSecurePolicy.SameAsRequest
            : CookieSecurePolicy.Always;
    }

    public static string BuildCookieName(string cookieName, CookieSecurePolicy securePolicy) =>
        securePolicy == CookieSecurePolicy.Always
            ? $"__Host-{cookieName}"
            : cookieName;

    public static void ConfigureLogging(
        ILoggingBuilder logging,
        IConfiguration configuration,
        StartupDiagnosticsOptions diagnosticsOptions)
    {
        logging.ClearProviders();
        logging.AddConfiguration(configuration.GetSection("Logging"));

        if (diagnosticsOptions.ConsoleLoggingEnabled)
        {
            logging.AddSimpleConsole(options =>
            {
                options.SingleLine = true;
                options.TimestampFormat = "yyyy-MM-dd HH:mm:ss ";
            });
            logging.AddFilter<Microsoft.Extensions.Logging.Console.ConsoleLoggerProvider>(null, diagnosticsOptions.ConsoleLogLevel);
        }

        logging.AddFilter(
            "Microsoft.EntityFrameworkCore.Database.Command",
            diagnosticsOptions.SqlLoggingEnabled ? diagnosticsOptions.SqlLogLevel : LogLevel.None);
    }

    public static void ConfigureApplicationServices(
        IServiceCollection services,
        IConfiguration configuration,
        string environmentName,
        ResolvedDatabaseConfiguration databaseConfiguration,
        StartupDiagnosticsOptions diagnosticsOptions)
    {
        services.AddSingleton(configuration);
        var authenticationSecurityOptions = BuildAuthenticationSecurityOptions(configuration);
        var cookieSecurePolicy = BuildCookieSecurePolicy(configuration, environmentName);
        var antiforgeryCookieName = BuildCookieName(AntiforgeryCookieName, cookieSecurePolicy);
        var authenticationCookieName = BuildCookieName(AuthenticationCookieName, cookieSecurePolicy);
        services.AddSingleton(authenticationSecurityOptions);
        services.AddHttpContextAccessor();
        services.AddAntiforgery(options =>
        {
            options.Cookie.Name = antiforgeryCookieName;
            options.Cookie.HttpOnly = true;
            options.Cookie.Path = "/";
            options.Cookie.SecurePolicy = cookieSecurePolicy;
            options.Cookie.SameSite = SameSiteMode.Strict;
        });
        services.AddDbContext<AppDbContext>(options =>
        {
            switch (databaseConfiguration.Provider)
            {
                case DatabaseProviderKind.SqlServer:
                    options.UseSqlServer(databaseConfiguration.ConnectionString);
                    break;
                case DatabaseProviderKind.Sqlite:
                default:
                    options.UseSqlite(databaseConfiguration.ConnectionString);
                    break;
            }

            if (diagnosticsOptions.SqlLoggingEnabled)
            {
                if (diagnosticsOptions.SqlDetailedErrorsEnabled)
                {
                    options.EnableDetailedErrors();
                }

                if (diagnosticsOptions.SqlSensitiveDataLoggingEnabled)
                {
                    options.EnableSensitiveDataLogging();
                }
            }
        });
        services.AddSingleton(databaseConfiguration);
        services.AddScoped<TrmWorkbookImportService>();
        services.AddScoped<SampleRelationshipImportService>();
        services.AddScoped<DatabaseInitializer>();
        services.AddScoped<CsvExportService>();
        services.AddScoped<AuditLogService>();
        services.AddScoped<AppSettingsService>();
        services.AddScoped<PasswordPolicyService>();
        services.AddScoped<PasswordHashService>();
        services.AddScoped<AppAuthenticationService>();
        services.AddScoped<ConfigurableFieldService>();
        services.AddScoped<ComponentVersioningService>();
        services.AddScoped<ConfiguredTimeZoneService>();
        services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(options =>
            {
                options.LoginPath = "/Account/Login";
                options.AccessDeniedPath = "/Account/Login";
                options.Cookie.Name = authenticationCookieName;
                options.Cookie.HttpOnly = true;
                options.Cookie.Path = "/";
                options.Cookie.SecurePolicy = cookieSecurePolicy;
                options.Cookie.SameSite = SameSiteMode.Strict;
                options.Cookie.IsEssential = true;
                options.SlidingExpiration = true;
                options.ExpireTimeSpan = authenticationSecurityOptions.SessionTimeout;
            });
        services.AddAuthorization(options =>
        {
            options.AddPolicy(AppPolicies.AdminOnly, policy =>
                policy.RequireAssertion(context => AppRoles.IsAdministrator(context.User)));
            options.AddPolicy(AppPolicies.CatalogueRead, policy =>
                policy.RequireAssertion(context => AppRoles.CanReadCatalogue(context.User)));
            options.AddPolicy(AppPolicies.ProductsAndServicesWrite, policy =>
                policy.RequireAssertion(context => AppRoles.CanManageProductsAndServices(context.User)));
            options.FallbackPolicy = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .Build();
        });
        services.AddControllersWithViews();
    }

    public static async Task InitializeDatabaseAsync(IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        var initializer = scope.ServiceProvider.GetRequiredService<DatabaseInitializer>();
        await initializer.InitializeAsync();
    }

    public static void ConfigurePipeline(WebApplication app)
    {
        if (!app.Environment.IsDevelopment())
        {
            app.UseExceptionHandler("/Home");
            app.UseHsts();
        }

        app.UseHttpsRedirection();
        app.UseRouting();

        app.UseAuthentication();
        app.UseAuthorization();

        app.MapStaticAssets().AllowAnonymous();

        app.MapControllerRoute(
            name: "default",
            pattern: "{controller=Home}/{action=Index}/{id?}")
            .WithStaticAssets();
    }

    public static LogLevel ParseLogLevel(string? value, LogLevel fallback) =>
        Enum.TryParse<LogLevel>(value, ignoreCase: true, out var parsedLevel)
            ? parsedLevel
            : fallback;
}
