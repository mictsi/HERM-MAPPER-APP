using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;

namespace HERM_MAPPER_APP.Configuration;

public enum DatabaseProviderKind
{
    Sqlite,
    SqlServer
}

public sealed class AppDatabaseSettings
{
    public const string SectionName = "Database";

    public string Provider { get; init; } = nameof(DatabaseProviderKind.Sqlite);
    public string? ConnectionString { get; init; }
    public string SqliteFilePath { get; init; } = "|DataDirectory|/herm-mapper.db";
}

public sealed record ResolvedDatabaseConfiguration(DatabaseProviderKind Provider, string ConnectionString);

public static class AppDatabaseConfiguration
{
    public static ResolvedDatabaseConfiguration Resolve(IConfiguration configuration, string contentRootPath)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentRootPath);

        var dataDirectory = Path.Combine(contentRootPath, "App_Data");
        var homeDirectory = ResolveHomeDirectory(contentRootPath);
        Directory.CreateDirectory(dataDirectory);

        var settings = configuration.GetSection(AppDatabaseSettings.SectionName).Get<AppDatabaseSettings>() ?? new AppDatabaseSettings();
        var provider = ParseProvider(settings.Provider);

        return provider switch
        {
            DatabaseProviderKind.Sqlite => new ResolvedDatabaseConfiguration(
                provider,
                ResolveSqliteConnectionString(configuration, settings, dataDirectory, homeDirectory)),
            DatabaseProviderKind.SqlServer => new ResolvedDatabaseConfiguration(
                provider,
                ResolveSqlServerConnectionString(configuration, settings)),
            _ => throw new InvalidOperationException($"Unsupported database provider '{settings.Provider}'.")
        };
    }

    public static DatabaseProviderKind ParseProvider(string? provider) =>
        provider?.Trim().ToLowerInvariant() switch
        {
            null or "" or "sqlite" => DatabaseProviderKind.Sqlite,
            "sqlserver" or "sql-server" or "mssql" => DatabaseProviderKind.SqlServer,
            _ => throw new InvalidOperationException($"Unsupported database provider '{provider}'.")
        };

    private static string ResolveSqliteConnectionString(
        IConfiguration configuration,
        AppDatabaseSettings settings,
        string dataDirectory,
        string homeDirectory)
    {
        var configuredConnectionString = FirstNonEmpty(
            settings.ConnectionString,
            configuration.GetConnectionString("Sqlite"),
            configuration.GetConnectionString("DefaultConnection"));

        if (!string.IsNullOrWhiteSpace(configuredConnectionString))
        {
            var resolvedConnectionString = ResolvePathTokens(configuredConnectionString, dataDirectory, homeDirectory);
            EnsureSqliteDataDirectoryExists(resolvedConnectionString);
            return resolvedConnectionString;
        }

        var sqliteFilePath = ResolvePathTokens(settings.SqliteFilePath, dataDirectory, homeDirectory);
        EnsureSqliteDataDirectoryExists($"Data Source={sqliteFilePath}");
        return $"Data Source={sqliteFilePath}";
    }

    private static string ResolveSqlServerConnectionString(
        IConfiguration configuration,
        AppDatabaseSettings settings)
    {
        var configuredConnectionString = FirstNonEmpty(
            settings.ConnectionString,
            configuration.GetConnectionString("SqlServer"),
            configuration.GetConnectionString("DefaultConnection"));

        if (string.IsNullOrWhiteSpace(configuredConnectionString))
        {
            throw new InvalidOperationException(
                "SQL Server was selected but no connection string was configured. Set Database:ConnectionString, ConnectionStrings:SqlServer, or ConnectionStrings:DefaultConnection.");
        }

        return configuredConnectionString;
    }

    private static string ResolvePathTokens(string value, string dataDirectory, string homeDirectory)
    {
        var resolvedValue = value
            .Replace("|DataDirectory|", dataDirectory.Replace("\\", "/"), StringComparison.OrdinalIgnoreCase)
            .Replace("|HomeDirectory|", homeDirectory.Replace("\\", "/"), StringComparison.OrdinalIgnoreCase);

        return ExpandEnvironmentVariables(resolvedValue);
    }

    private static string ResolveHomeDirectory(string contentRootPath) =>
        FirstNonEmpty(
            Environment.GetEnvironmentVariable("HOME"),
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            contentRootPath)
        ?? contentRootPath;

    private static string ExpandEnvironmentVariables(string value)
    {
        var expandedValue = Environment.ExpandEnvironmentVariables(value);

        return System.Text.RegularExpressions.Regex.Replace(
            expandedValue,
            @"\$(\{(?<name>[A-Za-z_][A-Za-z0-9_]*)\}|(?<name>[A-Za-z_][A-Za-z0-9_]*))",
            match =>
            {
                var variableName = match.Groups["name"].Value;
                return Environment.GetEnvironmentVariable(variableName) ?? match.Value;
            });
    }

    private static void EnsureSqliteDataDirectoryExists(string connectionString)
    {
        var builder = new SqliteConnectionStringBuilder(connectionString);
        var dataSource = builder.DataSource;

        if (string.IsNullOrWhiteSpace(dataSource) || string.Equals(dataSource, ":memory:", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var directory = Path.GetDirectoryName(dataSource);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
}
