using HERM_MAPPER_APP.Configuration;
using HERM_MAPPER_APP.Data;
using HERM_MAPPER_APP.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddEnvironmentVariables(prefix: "HERM_");

var databaseConfiguration = AppDatabaseConfiguration.Resolve(builder.Configuration, builder.Environment.ContentRootPath);
var consoleLoggingEnabled = builder.Configuration.GetValue<bool?>("Diagnostics:Console:Enabled") ?? true;
var consoleLogLevel = ParseLogLevel(builder.Configuration["Diagnostics:Console:LogLevel"], LogLevel.Information);
var sqlLoggingEnabled = builder.Configuration.GetValue<bool?>("Diagnostics:Sql:Enabled") ?? false;
var sqlLogLevel = ParseLogLevel(builder.Configuration["Diagnostics:Sql:LogLevel"], LogLevel.Information);
var sqlSensitiveDataLoggingEnabled = builder.Configuration.GetValue<bool?>("Diagnostics:Sql:IncludeSensitiveData") ?? false;
var sqlDetailedErrorsEnabled = builder.Configuration.GetValue<bool?>("Diagnostics:Sql:EnableDetailedErrors") ?? false;

builder.Logging.ClearProviders();
builder.Logging.AddConfiguration(builder.Configuration.GetSection("Logging"));
if (consoleLoggingEnabled)
{
    builder.Logging.AddSimpleConsole(options =>
    {
        options.SingleLine = true;
        options.TimestampFormat = "yyyy-MM-dd HH:mm:ss ";
    });
    builder.Logging.AddFilter<Microsoft.Extensions.Logging.Console.ConsoleLoggerProvider>(null, consoleLogLevel);
}

builder.Logging.AddFilter(
    "Microsoft.EntityFrameworkCore.Database.Command",
    sqlLoggingEnabled ? sqlLogLevel : LogLevel.None);

// Add services to the container.
builder.Services.AddDbContext<AppDbContext>(options =>
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

    if (sqlLoggingEnabled)
    {
        if (sqlDetailedErrorsEnabled)
        {
            options.EnableDetailedErrors();
        }

        if (sqlSensitiveDataLoggingEnabled)
        {
            options.EnableSensitiveDataLogging();
        }
    }
});
builder.Services.AddSingleton(databaseConfiguration);
builder.Services.AddScoped<TrmWorkbookImportService>();
builder.Services.AddScoped<SampleRelationshipImportService>();
builder.Services.AddScoped<DatabaseInitializer>();
builder.Services.AddScoped<CsvExportService>();
builder.Services.AddScoped<AuditLogService>();
builder.Services.AddScoped<ConfigurableFieldService>();
builder.Services.AddScoped<ComponentVersioningService>();
builder.Services.AddControllersWithViews();

var app = builder.Build();

await using (var scope = app.Services.CreateAsyncScope())
{
    var initializer = scope.ServiceProvider.GetRequiredService<DatabaseInitializer>();
    await initializer.InitializeAsync();
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
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


app.Run();

static LogLevel ParseLogLevel(string? value, LogLevel fallback) =>
    Enum.TryParse<LogLevel>(value, ignoreCase: true, out var parsedLevel)
        ? parsedLevel
        : fallback;
