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
        Directory.CreateDirectory(dataDirectory);

        var settings = configuration.GetSection(AppDatabaseSettings.SectionName).Get<AppDatabaseSettings>() ?? new AppDatabaseSettings();
        var provider = ParseProvider(settings.Provider);

        return provider switch
        {
            DatabaseProviderKind.Sqlite => new ResolvedDatabaseConfiguration(
                provider,
                ResolveSqliteConnectionString(configuration, settings, dataDirectory)),
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
        string dataDirectory)
    {
        var configuredConnectionString = FirstNonEmpty(
            settings.ConnectionString,
            configuration.GetConnectionString("Sqlite"),
            configuration.GetConnectionString("DefaultConnection"));

        if (!string.IsNullOrWhiteSpace(configuredConnectionString))
        {
            return ReplaceDataDirectoryToken(configuredConnectionString, dataDirectory);
        }

        return $"Data Source={ReplaceDataDirectoryToken(settings.SqliteFilePath, dataDirectory)}";
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

    private static string ReplaceDataDirectoryToken(string value, string dataDirectory) =>
        value.Replace("|DataDirectory|", dataDirectory.Replace("\\", "/"), StringComparison.OrdinalIgnoreCase);

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
}
