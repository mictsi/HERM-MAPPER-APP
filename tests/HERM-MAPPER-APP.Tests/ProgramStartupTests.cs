using HERM_MAPPER_APP.Configuration;
using HERM_MAPPER_APP.Data;
using HERM_MAPPER_APP.Models;
using HERM_MAPPER_APP.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace HERM_MAPPER_APP.Tests;

public sealed class ProgramStartupTests
{
    [Fact]
    public void BuildDiagnosticsOptions_UsesConfiguredValues_AndParsesLogLevels()
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
    public void ParseLogLevel_ReturnsFallback_WhenValueIsInvalid()
    {
        var parsed = Program.ParseLogLevel("not-a-level", LogLevel.Error);

        Assert.Equal(LogLevel.Error, parsed);
    }

    [Fact]
    public async Task ConfigureApplicationServices_RegistersSqliteServices_AndInitializeDatabaseAsyncSeedsDefaults()
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

        Program.ConfigureApplicationServices(services, configuration, databaseConfiguration, diagnosticsOptions);

        await using var provider = services.BuildServiceProvider();
        await Program.InitializeDatabaseAsync(provider);

        await using var scope = provider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var configurableFieldService = scope.ServiceProvider.GetRequiredService<ConfigurableFieldService>();

        Assert.Equal("Microsoft.EntityFrameworkCore.Sqlite", dbContext.Database.ProviderName);
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<AuditLogService>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<ComponentVersioningService>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<DatabaseInitializer>());

        var lifecycleStatuses = await configurableFieldService.GetOptionsAsync(ConfigurableFieldNames.LifecycleStatus);
        Assert.Equal(
            ConfigurableFieldNames.GetDefaultValues(ConfigurableFieldNames.LifecycleStatus),
            lifecycleStatuses.Select(x => x.Value).ToList());
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
