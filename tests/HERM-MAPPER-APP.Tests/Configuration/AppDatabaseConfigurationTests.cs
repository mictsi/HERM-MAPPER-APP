using HERM_MAPPER_APP.Configuration;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace HERM_MAPPER_APP.Tests.Configuration;

public sealed class AppDatabaseConfigurationTests
{
    [Fact]
    public void Resolve_UsesSqliteDefaults_WhenNoOverridesExist()
    {
        using var contentRoot = new TemporaryDirectory();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection()
            .Build();

        var resolved = AppDatabaseConfiguration.Resolve(configuration, contentRoot.Path);

        Assert.Equal(DatabaseProviderKind.Sqlite, resolved.Provider);
        Assert.Equal(
            NormalizeSlashes($"Data Source={Path.Combine(contentRoot.Path, "App_Data", "herm-mapper.db")}"),
            NormalizeSlashes(resolved.ConnectionString));
    }

    [Fact]
    public void Resolve_UsesPrefixedEnvironmentVariables_ForSqlServer()
    {
        using var contentRoot = new TemporaryDirectory();
        using var environment = new EnvironmentVariableScope(new Dictionary<string, string?>
        {
            ["HERM_Database__Provider"] = "SqlServer",
            ["HERM_Database__ConnectionString"] = "Server=localhost;Database=HermMapper;Trusted_Connection=True;TrustServerCertificate=True"
        });

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:Provider"] = "Sqlite",
                ["ConnectionStrings:DefaultConnection"] = "Data Source=ignored.db"
            })
            .AddEnvironmentVariables(prefix: "HERM_")
            .Build();

        var resolved = AppDatabaseConfiguration.Resolve(configuration, contentRoot.Path);

        Assert.Equal(DatabaseProviderKind.SqlServer, resolved.Provider);
        Assert.Equal(
            "Server=localhost;Database=HermMapper;Trusted_Connection=True;TrustServerCertificate=True",
            resolved.ConnectionString);
    }

    [Fact]
    public void Resolve_Throws_WhenSqlServerHasNoConnectionString()
    {
        using var contentRoot = new TemporaryDirectory();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:Provider"] = "SqlServer"
            })
            .Build();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            AppDatabaseConfiguration.Resolve(configuration, contentRoot.Path));

        Assert.Contains("SQL Server was selected", exception.Message);
    }

    private static string NormalizeSlashes(string value) => value.Replace("\\", "/");

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"herm-mapper-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }

    private sealed class EnvironmentVariableScope : IDisposable
    {
        private readonly IReadOnlyDictionary<string, string?> originalValues;
        private readonly IReadOnlyDictionary<string, string?> newValues;

        public EnvironmentVariableScope(IReadOnlyDictionary<string, string?> newValues)
        {
            this.newValues = new Dictionary<string, string?>(newValues);
            originalValues = this.newValues.Keys.ToDictionary(
                key => key,
                key => Environment.GetEnvironmentVariable(key));

            foreach (var (key, value) in this.newValues)
            {
                Environment.SetEnvironmentVariable(key, value);
            }
        }

        public void Dispose()
        {
            foreach (var key in newValues.Keys)
            {
                Environment.SetEnvironmentVariable(key, originalValues[key]);
            }
        }
    }
}
