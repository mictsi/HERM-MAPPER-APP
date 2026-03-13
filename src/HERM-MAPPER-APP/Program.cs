using HERM_MAPPER_APP.Configuration;
using HERM_MAPPER_APP.Data;
using HERM_MAPPER_APP.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddEnvironmentVariables(prefix: "HERM_");

var databaseConfiguration = Program.ResolveDatabaseConfiguration(builder.Configuration, builder.Environment.ContentRootPath);
var diagnosticsOptions = Program.BuildDiagnosticsOptions(builder.Configuration);

Program.ConfigureLogging(builder.Logging, builder.Configuration, diagnosticsOptions);
Program.ConfigureApplicationServices(builder.Services, builder.Configuration, databaseConfiguration, diagnosticsOptions);

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

public partial class Program
{
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
        ResolvedDatabaseConfiguration databaseConfiguration,
        StartupDiagnosticsOptions diagnosticsOptions)
    {
        services.AddSingleton(configuration);
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
        services.AddScoped<ConfigurableFieldService>();
        services.AddScoped<ComponentVersioningService>();
        services.AddScoped<ConfiguredTimeZoneService>();
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

        app.UseAuthorization();

        app.MapStaticAssets();

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
