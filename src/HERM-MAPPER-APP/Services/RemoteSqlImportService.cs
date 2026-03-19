using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using HERMMapperApp.Data;
using HERMMapperApp.Models;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HERMMapperApp.Services;

public sealed partial class RemoteSqlImportService(
    AppDbContext dbContext,
    AppSettingsService appSettingsService,
    ProtectedSettingsService protectedSettingsService,
    AuditLogService auditLogService,
    RemoteSqlImportExecutionGate executionGate,
    ILogger<RemoteSqlImportService> logger)
{
    private const string RemoteSqlImportCategory = "RemoteSqlImport";
    private const string StatusNotConfigured = "NotConfigured";
    private const string StatusRunning = "Running";
    private const string StatusSuccess = "Success";
    private const string StatusFailed = "Failed";

    public const string SectionKey = "remote-sql-import";

    private static readonly int[] AllowedScheduleHours = [0, 1, 3, 6, 12, 24];
    private static readonly IReadOnlyDictionary<string, IReadOnlySet<string>> RequiredSchema = new Dictionary<string, IReadOnlySet<string>>(StringComparer.OrdinalIgnoreCase)
    {
        ["ProductCatalogItems"] = new HashSet<string>(["Id", "Name", "Vendor", "Version", "LifecycleStatus", "Description", "Notes", "IsDeleted", "CreatedUtc", "UpdatedUtc"], StringComparer.OrdinalIgnoreCase),
        ["ProductMappings"] = new HashSet<string>(["Id", "ProductCatalogItemId", "TrmDomainId", "TrmCapabilityId", "TrmComponentId", "MappingStatus", "MappingRationale", "LastReviewedUtc", "CreatedUtc", "UpdatedUtc"], StringComparer.OrdinalIgnoreCase),
        ["TrmDomains"] = new HashSet<string>(["Id", "Code", "Name", "SourceTitle"], StringComparer.OrdinalIgnoreCase),
        ["TrmCapabilities"] = new HashSet<string>(["Id", "Code", "Name", "SourceTitle", "ParentDomainId"], StringComparer.OrdinalIgnoreCase),
        ["TrmComponents"] = new HashSet<string>(["Id", "Code", "TechnologyComponentCode", "Name", "SourceTitle", "ParentCapabilityId", "IsDeleted"], StringComparer.OrdinalIgnoreCase)
    };
    private static readonly IReadOnlyDictionary<string, IReadOnlySet<string>> OptionalSchema = new Dictionary<string, IReadOnlySet<string>>(StringComparer.OrdinalIgnoreCase)
    {
        ["ProductCatalogItemOwners"] = new HashSet<string>(["ProductCatalogItemId", "OwnerValue"], StringComparer.OrdinalIgnoreCase)
    };

    public static IReadOnlyList<int> GetAllowedScheduleHours() => AllowedScheduleHours;

    public static string BuildScheduleLabel(int scheduleHours) =>
        scheduleHours <= 0
            ? "Manual only"
            : $"Every {scheduleHours} hour{(scheduleHours == 1 ? string.Empty : "s")}";

    public async Task<RemoteSqlImportSettingsSnapshot> GetSettingsAsync(CancellationToken cancellationToken = default)
    {
        var legacyConnectionString = await appSettingsService.GetValueAsync(AppSettingKeys.RemoteSqlImportConnectionString, string.Empty, cancellationToken);
        var serverName = await appSettingsService.GetNullableValueAsync(AppSettingKeys.RemoteSqlImportServerName, cancellationToken);
        var databaseName = await appSettingsService.GetNullableValueAsync(AppSettingKeys.RemoteSqlImportDatabaseName, cancellationToken);
        var portValue = await appSettingsService.GetValueAsync(
            AppSettingKeys.RemoteSqlImportPort,
            AppSettingDefaults.RemoteSqlImportPort.ToString(CultureInfo.InvariantCulture),
            cancellationToken);
        var encryptValue = await appSettingsService.GetValueAsync(
            AppSettingKeys.RemoteSqlImportEncrypt,
            AppSettingDefaults.RemoteSqlImportEncrypt.ToString(),
            cancellationToken);
        var trustServerCertificateValue = await appSettingsService.GetValueAsync(
            AppSettingKeys.RemoteSqlImportTrustServerCertificate,
            AppSettingDefaults.RemoteSqlImportTrustServerCertificate.ToString(),
            cancellationToken);
        var useIntegratedSecurityValue = await appSettingsService.GetValueAsync(
            AppSettingKeys.RemoteSqlImportUseIntegratedSecurity,
            AppSettingDefaults.RemoteSqlImportUseIntegratedSecurity.ToString(),
            cancellationToken);
        var userName = await protectedSettingsService.GetValueAsync(AppSettingKeys.RemoteSqlImportUserName, cancellationToken);
        var password = await protectedSettingsService.GetValueAsync(AppSettingKeys.RemoteSqlImportPassword, cancellationToken);
        var scheduleHoursValue = await appSettingsService.GetValueAsync(
            AppSettingKeys.RemoteSqlImportScheduleHours,
            AppSettingDefaults.RemoteSqlImportScheduleHours.ToString(CultureInfo.InvariantCulture),
            cancellationToken);
        var lastAttemptUtc = await ReadDateTimeSettingAsync(AppSettingKeys.RemoteSqlImportLastAttemptUtc, cancellationToken);
        var lastSuccessUtc = await ReadDateTimeSettingAsync(AppSettingKeys.RemoteSqlImportLastSuccessUtc, cancellationToken);
        var statusCode = await appSettingsService.GetValueAsync(AppSettingKeys.RemoteSqlImportLastStatus, StatusNotConfigured, cancellationToken);
        var lastMessage = await appSettingsService.GetNullableValueAsync(AppSettingKeys.RemoteSqlImportLastMessage, cancellationToken);

        var scheduleHours = int.TryParse(scheduleHoursValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedScheduleHours)
            && AllowedScheduleHours.Contains(parsedScheduleHours)
                ? parsedScheduleHours
                : AppSettingDefaults.RemoteSqlImportScheduleHours;

        var port = int.TryParse(portValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedPort) && parsedPort is > 0 and <= 65535
            ? parsedPort
            : AppSettingDefaults.RemoteSqlImportPort;
        var encrypt = bool.TryParse(encryptValue, out var parsedEncrypt)
            ? parsedEncrypt
            : AppSettingDefaults.RemoteSqlImportEncrypt;
        var trustServerCertificate = bool.TryParse(trustServerCertificateValue, out var parsedTrustServerCertificate)
            ? parsedTrustServerCertificate
            : AppSettingDefaults.RemoteSqlImportTrustServerCertificate;
        var useIntegratedSecurity = bool.TryParse(useIntegratedSecurityValue, out var parsedUseIntegratedSecurity)
            ? parsedUseIntegratedSecurity
            : AppSettingDefaults.RemoteSqlImportUseIntegratedSecurity;

        if ((string.IsNullOrWhiteSpace(serverName) || string.IsNullOrWhiteSpace(databaseName)) &&
            TryParseLegacyConnectionString(
                legacyConnectionString,
                out var legacyServerName,
                out var legacyPort,
                out var legacyDatabaseName,
                out var legacyEncrypt,
                out var legacyTrustServerCertificate,
                out var legacyUseIntegratedSecurity,
                out var legacyUserName,
                out var legacyPassword))
        {
            serverName ??= legacyServerName;
            databaseName ??= legacyDatabaseName;
            port = legacyPort ?? port;
            encrypt = legacyEncrypt ?? encrypt;
            trustServerCertificate = legacyTrustServerCertificate ?? trustServerCertificate;
            useIntegratedSecurity = legacyUseIntegratedSecurity ?? useIntegratedSecurity;
            userName ??= legacyUserName;
            password ??= legacyPassword;
        }

        var isConfigured = !string.IsNullOrWhiteSpace(serverName) && !string.IsNullOrWhiteSpace(databaseName);
        if (!isConfigured)
        {
            statusCode = StatusNotConfigured;
            lastMessage ??= "Remote SQL import has not been configured yet.";
        }

        return new RemoteSqlImportSettingsSnapshot
        {
            ServerName = serverName?.Trim() ?? string.Empty,
            Port = port,
            DatabaseName = databaseName?.Trim() ?? string.Empty,
            Encrypt = encrypt,
            TrustServerCertificate = trustServerCertificate,
            UseIntegratedSecurity = useIntegratedSecurity,
            UserName = userName,
            Password = password,
            ScheduleHours = scheduleHours,
            LastAttemptUtc = lastAttemptUtc,
            LastSuccessUtc = lastSuccessUtc,
            StatusCode = statusCode,
            LastMessage = lastMessage,
            IsConfigured = isConfigured
        };
    }

    public async Task<RemoteSqlImportSaveResult> SaveSettingsAsync(
        RemoteSqlImportConfigurationInput input,
        CancellationToken cancellationToken = default)
    {
        var currentSettings = await GetSettingsAsync(cancellationToken);
        var resolvedInput = ResolveInput(input, currentSettings, useStoredCredentialsAsFallback: true, preserveStoredCredentialsWhenBlank: true);

        if (resolvedInput.Errors.Count > 0)
        {
            var message = string.Join(" ", resolvedInput.Errors);
            await LogConfigurationFailureAsync("SaveConfiguration", "Failed to save remote SQL import settings.", message);
            return RemoteSqlImportSaveResult.Failure(message);
        }

        try
        {
            var connectionTestResult = await ValidateResolvedConnectionAsync(
                resolvedInput,
                "SaveConfiguration",
                "Remote SQL settings were not saved because the connection test failed.",
                cancellationToken);

            if (!connectionTestResult.IsSuccess)
            {
                return RemoteSqlImportSaveResult.Failure(connectionTestResult.Message);
            }

            await appSettingsService.SetValueAsync(AppSettingKeys.RemoteSqlImportServerName, resolvedInput.ServerName, cancellationToken);
            await appSettingsService.SetValueAsync(AppSettingKeys.RemoteSqlImportPort, resolvedInput.Port.ToString(CultureInfo.InvariantCulture), cancellationToken);
            await appSettingsService.SetValueAsync(AppSettingKeys.RemoteSqlImportDatabaseName, resolvedInput.DatabaseName, cancellationToken);
            await appSettingsService.SetValueAsync(AppSettingKeys.RemoteSqlImportEncrypt, resolvedInput.Encrypt.ToString(), cancellationToken);
            await appSettingsService.SetValueAsync(AppSettingKeys.RemoteSqlImportTrustServerCertificate, resolvedInput.TrustServerCertificate.ToString(), cancellationToken);
            await appSettingsService.SetValueAsync(AppSettingKeys.RemoteSqlImportUseIntegratedSecurity, resolvedInput.UseIntegratedSecurity.ToString(), cancellationToken);
            await appSettingsService.SetValueAsync(AppSettingKeys.RemoteSqlImportConnectionString, string.Empty, cancellationToken);
            await appSettingsService.SetValueAsync(
                AppSettingKeys.RemoteSqlImportScheduleHours,
                resolvedInput.ScheduleHours.ToString(CultureInfo.InvariantCulture),
                cancellationToken);

            if (resolvedInput.ClearStoredCredentials)
            {
                await protectedSettingsService.SetValueAsync(AppSettingKeys.RemoteSqlImportUserName, null, cancellationToken);
                await protectedSettingsService.SetValueAsync(AppSettingKeys.RemoteSqlImportPassword, null, cancellationToken);
            }
            else
            {
                if (resolvedInput.UserNameChanged)
                {
                    await protectedSettingsService.SetValueAsync(AppSettingKeys.RemoteSqlImportUserName, resolvedInput.EffectiveUserName, cancellationToken);
                }

                if (resolvedInput.PasswordChanged)
                {
                    await protectedSettingsService.SetValueAsync(AppSettingKeys.RemoteSqlImportPassword, resolvedInput.EffectivePassword, cancellationToken);
                }
            }

            await auditLogService.WriteAsync(
                RemoteSqlImportCategory,
                "SaveConfiguration",
                nameof(AppSetting),
                null,
                "Saved remote SQL import settings.",
                $"Server: {resolvedInput.ServerName}:{resolvedInput.Port}; database: {resolvedInput.DatabaseName}; encryption: {resolvedInput.ConnectionSecurityMode}; authentication: {resolvedInput.CredentialStorageMode}; schedule: {BuildScheduleLabel(resolvedInput.ScheduleHours)}. {connectionTestResult.Message}");

            logger.LogInformation(
                "Saved remote SQL import settings with schedule {ScheduleHours} for server {ServerName}:{Port} and database {DatabaseName}.",
                resolvedInput.ScheduleHours,
                resolvedInput.ServerName,
                resolvedInput.Port,
                resolvedInput.DatabaseName);

            return RemoteSqlImportSaveResult.Success(
                $"Remote SQL connection validated and settings saved. Schedule: {BuildScheduleLabel(resolvedInput.ScheduleHours)}.",
                resolvedInput.NewlySavedUserName,
                resolvedInput.NewlySavedPassword);
        }
        catch (Exception exception)
        {
            const string message = "Remote SQL import settings could not be saved.";
            logger.LogError(exception, message);
            await auditLogService.WriteAsync(
                RemoteSqlImportCategory,
                "SaveConfiguration",
                nameof(AppSetting),
                null,
                message,
                exception.Message);
            return RemoteSqlImportSaveResult.Failure($"{message} {exception.Message}");
        }
    }

    public async Task<RemoteSqlImportConnectionTestResult> TestConnectionAsync(
        RemoteSqlImportConfigurationInput input,
        CancellationToken cancellationToken = default)
    {
        var currentSettings = await GetSettingsAsync(cancellationToken);
        var resolvedInput = ResolveInput(input, currentSettings, useStoredCredentialsAsFallback: true, preserveStoredCredentialsWhenBlank: true);

        if (resolvedInput.Errors.Count > 0)
        {
            var message = string.Join(" ", resolvedInput.Errors);
            await LogConfigurationFailureAsync("TestConnection", "Remote SQL connection test failed validation.", message);
            return RemoteSqlImportConnectionTestResult.Failure(message, resolvedInput.Errors);
        }

        return await ValidateResolvedConnectionAsync(
            resolvedInput,
            "TestConnection",
            "Remote SQL connection test failed.",
            cancellationToken);
    }

    public async Task<RemoteSqlImportRunResult> RunManualImportAsync(CancellationToken cancellationToken = default)
    {
        var settings = await GetSettingsAsync(cancellationToken);
        return await RunImportAsync(settings, RemoteSqlImportTrigger.Manual, cancellationToken);
    }

    public async Task RunScheduledImportIfDueAsync(CancellationToken cancellationToken = default)
    {
        var settings = await GetSettingsAsync(cancellationToken);
        if (!settings.IsConfigured || settings.ScheduleHours <= 0)
        {
            return;
        }

        var nextRunUtc = settings.NextScheduledRunUtc;
        if (nextRunUtc.HasValue && nextRunUtc.Value > DateTime.UtcNow)
        {
            return;
        }

        await RunImportAsync(settings, RemoteSqlImportTrigger.Scheduled, cancellationToken);
    }

    private async Task<RemoteSqlImportRunResult> RunImportAsync(
        RemoteSqlImportSettingsSnapshot settings,
        RemoteSqlImportTrigger trigger,
        CancellationToken cancellationToken)
    {
        if (!settings.IsConfigured)
        {
            var message = "Save the remote SQL connection settings before running an import.";
            await auditLogService.WriteAsync(
                RemoteSqlImportCategory,
                trigger == RemoteSqlImportTrigger.Manual ? "ImportManual" : "ImportScheduled",
                nameof(ProductCatalogItem),
                null,
                "Remote SQL import was skipped.",
                message);
            return RemoteSqlImportRunResult.Failure(message);
        }

        if (!await executionGate.Semaphore.WaitAsync(TimeSpan.Zero, cancellationToken))
        {
            return RemoteSqlImportRunResult.Failure("A remote SQL import is already in progress.");
        }

        try
        {
            await UpdateExecutionStatusAsync(DateTime.UtcNow, null, StatusRunning, $"{trigger} remote SQL import in progress.", cancellationToken);

            await using var connection = new SqlConnection(settings.EffectiveConnectionString);
            await connection.OpenAsync(cancellationToken);

            var schemaValidation = await ValidateRemoteSchemaAsync(connection, cancellationToken);
            if (!schemaValidation.IsSuccess)
            {
                var message = "Remote SQL import failed because the source schema is incomplete.";
                var details = string.Join(" | ", schemaValidation.Errors);
                await UpdateExecutionStatusAsync(DateTime.UtcNow, settings.LastSuccessUtc, StatusFailed, $"{message} {details}", cancellationToken);
                await auditLogService.WriteAsync(
                    RemoteSqlImportCategory,
                    trigger == RemoteSqlImportTrigger.Manual ? "ImportManual" : "ImportScheduled",
                    nameof(ProductCatalogItem),
                    null,
                    message,
                    details);
                return RemoteSqlImportRunResult.Failure($"{message} {details}");
            }

            var remoteSnapshot = await ReadRemoteSnapshotAsync(connection, schemaValidation, cancellationToken);
            var importSummary = await ApplyImportSnapshotAsync(remoteSnapshot, cancellationToken);

            var summaryMessage =
                $"Imported remote SQL data. Products +{importSummary.ProductsAdded} added, {importSummary.ProductsUpdated} updated, " +
                $"{importSummary.ProductsMatched} unchanged; mappings +{importSummary.MappingsAdded} added, {importSummary.MappingsUpdated} updated, " +
                $"{importSummary.MappingsSkipped} skipped.";
            var detailMessage =
                $"Remote products read: {importSummary.RemoteProductsRead}; remote mappings read: {importSummary.RemoteMappingsRead}; " +
                $"owner sets updated: {importSummary.OwnerSetsUpdated}.";

            if (importSummary.Warnings.Count > 0)
            {
                detailMessage = $"{detailMessage} Warnings: {string.Join(" | ", importSummary.Warnings.Take(10))}";
            }

            var completedUtc = DateTime.UtcNow;
            await UpdateExecutionStatusAsync(completedUtc, completedUtc, StatusSuccess, summaryMessage, cancellationToken);
            await auditLogService.WriteAsync(
                RemoteSqlImportCategory,
                trigger == RemoteSqlImportTrigger.Manual ? "ImportManual" : "ImportScheduled",
                nameof(ProductCatalogItem),
                null,
                summaryMessage,
                detailMessage);

            return RemoteSqlImportRunResult.Success(summaryMessage, importSummary.Warnings);
        }
        catch (Exception exception)
        {
            dbContext.ChangeTracker.Clear();

            var message = $"Remote SQL import failed. {exception.Message}";
            logger.LogError(exception, "Remote SQL import failed during {Trigger} execution.", trigger);
            await UpdateExecutionStatusAsync(DateTime.UtcNow, settings.LastSuccessUtc, StatusFailed, message, cancellationToken);
            await auditLogService.WriteAsync(
                RemoteSqlImportCategory,
                trigger == RemoteSqlImportTrigger.Manual ? "ImportManual" : "ImportScheduled",
                nameof(ProductCatalogItem),
                null,
                "Remote SQL import failed.",
                exception.Message);
            return RemoteSqlImportRunResult.Failure(message);
        }
        finally
        {
            executionGate.Semaphore.Release();
        }
    }
}

public sealed partial class RemoteSqlImportService
{
    private async Task<RemoteSqlImportConnectionTestResult> ValidateResolvedConnectionAsync(
        ResolvedConnectionInput resolvedInput,
        string auditAction,
        string failureSummary,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = new SqlConnection(resolvedInput.EffectiveConnectionString);
            await connection.OpenAsync(cancellationToken);

            var schemaValidation = await ValidateRemoteSchemaAsync(connection, cancellationToken);
            if (!schemaValidation.IsSuccess)
            {
                var message = "Remote SQL connection succeeded, but the required schema is missing.";
                await auditLogService.WriteAsync(
                    RemoteSqlImportCategory,
                    auditAction,
                    nameof(AppSetting),
                    null,
                    failureSummary,
                    string.Join(" | ", schemaValidation.Errors));

                return RemoteSqlImportConnectionTestResult.Failure(
                    message,
                    schemaValidation.Errors,
                    schemaValidation.Warnings);
            }

            var counts = await ReadRemoteCountsAsync(connection, schemaValidation.TableSchemas, cancellationToken);
            var summary = $"Connection succeeded. Found {counts.ProductCount} product(s) and {counts.MappingCount} mapping(s) in the remote database.";

            if (string.Equals(auditAction, "TestConnection", StringComparison.Ordinal))
            {
                await auditLogService.WriteAsync(
                    RemoteSqlImportCategory,
                    auditAction,
                    nameof(AppSetting),
                    null,
                    "Validated remote SQL import connection.",
                    $"{summary} Owners table present: {schemaValidation.OwnersTableAvailable}.");
            }

            return RemoteSqlImportConnectionTestResult.Success(
                summary,
                counts.ProductCount,
                counts.MappingCount,
                schemaValidation.OwnersTableAvailable,
                schemaValidation.Warnings);
        }
        catch (Exception exception)
        {
            var message = $"Remote SQL connection test failed. {exception.Message}";
            logger.LogError(exception, "Remote SQL connection validation failed for action {AuditAction}.", auditAction);
            await auditLogService.WriteAsync(
                RemoteSqlImportCategory,
                auditAction,
                nameof(AppSetting),
                null,
                failureSummary,
                exception.Message);
            return RemoteSqlImportConnectionTestResult.Failure(message, [exception.Message]);
        }
    }

    private async Task<DateTime?> ReadDateTimeSettingAsync(string key, CancellationToken cancellationToken)
    {
        var rawValue = await appSettingsService.GetNullableValueAsync(key, cancellationToken);
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return null;
        }

        return DateTime.TryParse(rawValue, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsedValue)
            ? parsedValue
            : null;
    }

    private async Task UpdateExecutionStatusAsync(
        DateTime? lastAttemptUtc,
        DateTime? lastSuccessUtc,
        string statusCode,
        string? message,
        CancellationToken cancellationToken)
    {
        if (lastAttemptUtc.HasValue)
        {
            await appSettingsService.SetValueAsync(
                AppSettingKeys.RemoteSqlImportLastAttemptUtc,
                lastAttemptUtc.Value.ToString("O", CultureInfo.InvariantCulture),
                cancellationToken);
        }

        if (lastSuccessUtc.HasValue)
        {
            await appSettingsService.SetValueAsync(
                AppSettingKeys.RemoteSqlImportLastSuccessUtc,
                lastSuccessUtc.Value.ToString("O", CultureInfo.InvariantCulture),
                cancellationToken);
        }

        await appSettingsService.SetValueAsync(AppSettingKeys.RemoteSqlImportLastStatus, statusCode, cancellationToken);
        await appSettingsService.SetValueAsync(AppSettingKeys.RemoteSqlImportLastMessage, message ?? string.Empty, cancellationToken);
    }

    private async Task LogConfigurationFailureAsync(string action, string summary, string details)
    {
        logger.LogWarning("{Summary} {Details}", summary, details);
        await auditLogService.WriteAsync(
            RemoteSqlImportCategory,
            action,
            nameof(AppSetting),
            null,
            summary,
            details);
    }

    private ResolvedConnectionInput ResolveInput(
        RemoteSqlImportConfigurationInput input,
        RemoteSqlImportSettingsSnapshot currentSettings,
        bool useStoredCredentialsAsFallback,
        bool preserveStoredCredentialsWhenBlank)
    {
        var errors = new List<string>();
        var serverName = input.ServerName?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(serverName))
        {
            errors.Add("Enter the remote SQL Server name.");
        }

        var databaseName = input.DatabaseName?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(databaseName))
        {
            errors.Add("Enter the remote SQL Server database name.");
        }

        if (input.Port is <= 0 or > 65535)
        {
            errors.Add("Enter a valid SQL Server port.");
        }

        if (!AllowedScheduleHours.Contains(input.ScheduleHours))
        {
            errors.Add("Choose a valid import schedule.");
        }

        var typedUserName = string.IsNullOrWhiteSpace(input.UserName) ? null : input.UserName.Trim();
        var typedPassword = string.IsNullOrWhiteSpace(input.Password) ? null : input.Password;
        var usesIntegratedSecurity = input.UseIntegratedSecurity;

        if ((typedUserName is null) != (typedPassword is null))
        {
            errors.Add("Enter both user name and password, or leave both blank.");
        }

        var effectiveUserName = typedUserName;
        var effectivePassword = typedPassword;

        if (!usesIntegratedSecurity && useStoredCredentialsAsFallback && typedUserName is null && typedPassword is null)
        {
            effectiveUserName ??= currentSettings.UserName;
            effectivePassword ??= currentSettings.Password;
        }

        if (!usesIntegratedSecurity && (effectiveUserName is null) != (effectivePassword is null))
        {
            errors.Add("The effective credentials are incomplete. Supply both a user name and a password.");
        }

        if (!usesIntegratedSecurity && effectiveUserName is null && effectivePassword is null)
        {
            errors.Add("Enter a user name and password for SQL login, or choose integrated security.");
        }

        if (errors.Count > 0)
        {
            return ResolvedConnectionInput.Invalid(errors);
        }

        var clearStoredCredentials = usesIntegratedSecurity;

        if (!usesIntegratedSecurity && !clearStoredCredentials && preserveStoredCredentialsWhenBlank)
        {
            if (typedUserName is null && typedPassword is null)
            {
                effectiveUserName ??= currentSettings.UserName;
                effectivePassword ??= currentSettings.Password;
            }
        }

        var effectiveConnectionString = BuildRemoteConnectionString(
            serverName,
            input.Port,
            databaseName,
            input.Encrypt,
            input.TrustServerCertificate,
            usesIntegratedSecurity,
            effectiveUserName,
            effectivePassword);

        return ResolvedConnectionInput.Valid(
            serverName,
            input.Port,
            databaseName,
            input.Encrypt,
            input.TrustServerCertificate,
            usesIntegratedSecurity,
            effectiveUserName,
            effectivePassword,
            effectiveConnectionString,
            input.ScheduleHours,
            clearStoredCredentials,
            usesIntegratedSecurity ? null : typedUserName,
            usesIntegratedSecurity ? null : typedPassword,
            typedUserName is not null || clearStoredCredentials,
            typedPassword is not null || clearStoredCredentials);
    }
}

public sealed partial class RemoteSqlImportService
{
    internal static string BuildRemoteConnectionString(
        string serverName,
        int port,
        string databaseName,
        bool encrypt,
        bool trustServerCertificate,
        bool useIntegratedSecurity,
        string? userName,
        string? password)
    {
        var builder = new SqlConnectionStringBuilder
        {
            DataSource = BuildDataSource(serverName, port),
            InitialCatalog = databaseName,
            Encrypt = encrypt,
            TrustServerCertificate = trustServerCertificate,
            IntegratedSecurity = useIntegratedSecurity
        };

        if (!useIntegratedSecurity && !string.IsNullOrWhiteSpace(userName) && !string.IsNullOrWhiteSpace(password))
        {
            builder.UserID = userName;
            builder.Password = password;
            builder.IntegratedSecurity = false;
        }

        return builder.ConnectionString;
    }

    private static bool TryParseLegacyConnectionString(
        string? connectionString,
        out string? serverName,
        out int? port,
        out string? databaseName,
        out bool? encrypt,
        out bool? trustServerCertificate,
        out bool? useIntegratedSecurity,
        out string? userName,
        out string? password)
    {
        serverName = null;
        port = null;
        databaseName = null;
        encrypt = null;
        trustServerCertificate = null;
        useIntegratedSecurity = null;
        userName = null;
        password = null;

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return false;
        }

        try
        {
            var builder = new SqlConnectionStringBuilder(connectionString);
            serverName = ExtractServerName(builder.DataSource);
            port = ExtractPort(builder.DataSource);
            databaseName = string.IsNullOrWhiteSpace(builder.InitialCatalog) ? null : builder.InitialCatalog.Trim();
            encrypt = builder.Encrypt;
            trustServerCertificate = builder.TrustServerCertificate;
            useIntegratedSecurity = builder.IntegratedSecurity;
            userName = string.IsNullOrWhiteSpace(builder.UserID) ? null : builder.UserID.Trim();
            password = string.IsNullOrWhiteSpace(builder.Password) ? null : builder.Password;
            return !string.IsNullOrWhiteSpace(serverName) || !string.IsNullOrWhiteSpace(databaseName);
        }
        catch
        {
            return false;
        }
    }

    private static string BuildDataSource(string serverName, int port)
    {
        var normalizedServerName = serverName.Trim();
        if (normalizedServerName.StartsWith("tcp:", StringComparison.OrdinalIgnoreCase))
        {
            normalizedServerName = normalizedServerName[4..];
        }

        return $"tcp:{normalizedServerName},{port}";
    }

    private static string? ExtractServerName(string? dataSource)
    {
        if (string.IsNullOrWhiteSpace(dataSource))
        {
            return null;
        }

        var normalizedDataSource = dataSource.Trim();
        if (normalizedDataSource.StartsWith("tcp:", StringComparison.OrdinalIgnoreCase))
        {
            normalizedDataSource = normalizedDataSource[4..];
        }

        var commaIndex = normalizedDataSource.LastIndexOf(',');
        if (commaIndex <= 0)
        {
            return normalizedDataSource;
        }

        return normalizedDataSource[..commaIndex].Trim();
    }

    private static int? ExtractPort(string? dataSource)
    {
        if (string.IsNullOrWhiteSpace(dataSource))
        {
            return null;
        }

        var normalizedDataSource = dataSource.Trim();
        if (normalizedDataSource.StartsWith("tcp:", StringComparison.OrdinalIgnoreCase))
        {
            normalizedDataSource = normalizedDataSource[4..];
        }

        var commaIndex = normalizedDataSource.LastIndexOf(',');
        if (commaIndex <= 0 || commaIndex == normalizedDataSource.Length - 1)
        {
            return null;
        }

        return int.TryParse(normalizedDataSource[(commaIndex + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedPort)
            ? parsedPort
            : null;
    }

    private static string BuildQualifiedTableName(IReadOnlyDictionary<string, string> tableSchemas, string tableName) =>
        $"[{tableSchemas[tableName]}].[{tableName}]";

    private static async Task<int> ExecuteScalarIntAsync(SqlConnection connection, string commandText, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = commandText;
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is null || result == DBNull.Value ? 0 : Convert.ToInt32(result, CultureInfo.InvariantCulture);
    }

    private static string? ReadTrimmedString(SqlDataReader reader, int ordinal)
    {
        if (reader.IsDBNull(ordinal))
        {
            return null;
        }

        var value = reader.GetString(ordinal).Trim();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static DateTime ReadDateTime(SqlDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? DateTime.UtcNow : reader.GetDateTime(ordinal);

    private static DateTime? ReadNullableDateTime(SqlDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetDateTime(ordinal);

    private static int? ReadNullableInt(SqlDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetInt32(ordinal);

    private static string BuildMappingKey(string productName, int? domainId, int? capabilityId, int? componentId) =>
        $"{productName.Trim()}|{domainId?.ToString(CultureInfo.InvariantCulture) ?? "-"}|{capabilityId?.ToString(CultureInfo.InvariantCulture) ?? "-"}|{componentId?.ToString(CultureInfo.InvariantCulture) ?? "-"}";

    private static MappingStatus ResolveMappingStatus(int? rawValue) =>
        rawValue.HasValue && Enum.IsDefined(typeof(MappingStatus), rawValue.Value)
            ? (MappingStatus)rawValue.Value
            : MappingStatus.Complete;

    private static bool ApplyStringChange(string? sourceValue, int maxLength, Action<string?> setter, string? currentValue)
    {
        var trimmedValue = TrimToLength(sourceValue, maxLength);
        if (string.Equals(currentValue, trimmedValue, StringComparison.Ordinal))
        {
            return false;
        }

        setter(trimmedValue);
        return true;
    }

    private static string? TrimToLength(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmedValue = value.Trim();
        return trimmedValue.Length <= maxLength
            ? trimmedValue
            : trimmedValue[..maxLength];
    }

    private sealed record ResolvedRelationshipMapping(TrmDomain Domain, TrmCapability Capability, TrmComponent Component);
    private readonly record struct RemoteSqlCounts(int ProductCount, int MappingCount);
}

public sealed record RemoteSqlImportConfigurationInput(
    string ServerName,
    int Port,
    string DatabaseName,
    bool Encrypt,
    bool TrustServerCertificate,
    bool UseIntegratedSecurity,
    string? UserName,
    string? Password,
    int ScheduleHours);

public sealed class RemoteSqlImportSettingsSnapshot
{
    public string ServerName { get; init; } = string.Empty;
    public int Port { get; init; } = AppSettingDefaults.RemoteSqlImportPort;
    public string DatabaseName { get; init; } = string.Empty;
    public bool Encrypt { get; init; } = AppSettingDefaults.RemoteSqlImportEncrypt;
    public bool TrustServerCertificate { get; init; } = AppSettingDefaults.RemoteSqlImportTrustServerCertificate;
    public bool UseIntegratedSecurity { get; init; } = AppSettingDefaults.RemoteSqlImportUseIntegratedSecurity;
    public string? UserName { get; init; }
    public string? Password { get; init; }
    public int ScheduleHours { get; init; }
    public DateTime? LastAttemptUtc { get; init; }
    public DateTime? LastSuccessUtc { get; init; }
    public string StatusCode { get; init; } = "NotConfigured";
    public string? LastMessage { get; init; }
    public bool IsConfigured { get; init; }

    public bool HasSavedUserName => !string.IsNullOrWhiteSpace(UserName);
    public bool HasSavedPassword => !string.IsNullOrWhiteSpace(Password);
    public string MaskedUserName => HasSavedUserName ? Mask(UserName!) : "Not stored";
    public string MaskedPassword => HasSavedPassword ? "Stored and hidden" : "Not stored";
    public string ScheduleLabel => RemoteSqlImportService.BuildScheduleLabel(ScheduleHours);
    public string StatusLabel => StatusCode switch
    {
        "Running" => "Import in progress",
        "Success" => "Last run succeeded",
        "Failed" => "Last run failed",
        _ when !IsConfigured => "Not configured",
        _ => "Ready"
    };
    public DateTime? NextScheduledRunUtc => ScheduleHours <= 0
        ? null
        : LastAttemptUtc?.AddHours(ScheduleHours) ?? DateTime.UtcNow;

    public string EffectiveConnectionString
    {
        get
        {
            return RemoteSqlImportService.BuildRemoteConnectionString(
                ServerName,
                Port,
                DatabaseName,
                Encrypt,
                TrustServerCertificate,
                UseIntegratedSecurity,
                UserName,
                Password);
        }
    }

    private static string Mask(string value)
    {
        if (value.Length <= 2)
        {
            return new string('*', value.Length);
        }

        return $"{value[..2]}{new string('*', Math.Max(2, value.Length - 2))}";
    }
}

public sealed class RemoteSqlImportSaveResult
{
    public bool IsSuccess { get; init; }
    public string Message { get; init; } = string.Empty;
    public string? SavedUserNameClearText { get; init; }
    public string? SavedPasswordClearText { get; init; }

    public static RemoteSqlImportSaveResult Success(string message, string? savedUserNameClearText, string? savedPasswordClearText) =>
        new()
        {
            IsSuccess = true,
            Message = message,
            SavedUserNameClearText = savedUserNameClearText,
            SavedPasswordClearText = savedPasswordClearText
        };

    public static RemoteSqlImportSaveResult Failure(string message) =>
        new()
        {
            IsSuccess = false,
            Message = message
        };
}

public sealed class RemoteSqlImportConnectionTestResult
{
    public bool IsSuccess { get; init; }
    public string Message { get; init; } = string.Empty;
    public int RemoteProductCount { get; init; }
    public int RemoteMappingCount { get; init; }
    public bool OwnersTableAvailable { get; init; }
    public IReadOnlyList<string> Errors { get; init; } = [];
    public IReadOnlyList<string> Warnings { get; init; } = [];

    public static RemoteSqlImportConnectionTestResult Success(
        string message,
        int remoteProductCount,
        int remoteMappingCount,
        bool ownersTableAvailable,
        IReadOnlyList<string> warnings) =>
        new()
        {
            IsSuccess = true,
            Message = message,
            RemoteProductCount = remoteProductCount,
            RemoteMappingCount = remoteMappingCount,
            OwnersTableAvailable = ownersTableAvailable,
            Warnings = warnings
        };

    public static RemoteSqlImportConnectionTestResult Failure(
        string message,
        IReadOnlyList<string> errors,
        IReadOnlyList<string>? warnings = null) =>
        new()
        {
            IsSuccess = false,
            Message = message,
            Errors = errors,
            Warnings = warnings ?? []
        };
}

public sealed class RemoteSqlImportRunResult
{
    public bool IsSuccess { get; init; }
    public string Message { get; init; } = string.Empty;
    public IReadOnlyList<string> Warnings { get; init; } = [];

    public static RemoteSqlImportRunResult Success(string message, IReadOnlyList<string> warnings) =>
        new()
        {
            IsSuccess = true,
            Message = message,
            Warnings = warnings
        };

    public static RemoteSqlImportRunResult Failure(string message) =>
        new()
        {
            IsSuccess = false,
            Message = message
        };
}

internal sealed class RemoteSqlImportSummary
{
    public int RemoteProductsRead { get; set; }
    public int RemoteMappingsRead { get; set; }
    public int ProductsAdded { get; set; }
    public int ProductsUpdated { get; set; }
    public int ProductsMatched { get; set; }
    public int MappingsAdded { get; set; }
    public int MappingsUpdated { get; set; }
    public int MappingsSkipped { get; set; }
    public int OwnerSetsUpdated { get; set; }
    public List<string> Warnings { get; } = [];
}

internal sealed class RemoteSqlSnapshot(
    IReadOnlyList<RemoteProductRow> products,
    IReadOnlyDictionary<int, IReadOnlyList<string>> ownersByProductId,
    IReadOnlyList<RemoteMappingRow> mappings,
    bool ownersTableAvailable,
    IReadOnlyList<string> schemaWarnings)
{
    public IReadOnlyList<RemoteProductRow> Products { get; } = products;
    public IReadOnlyDictionary<int, IReadOnlyList<string>> OwnersByProductId { get; } = ownersByProductId;
    public IReadOnlyList<RemoteMappingRow> Mappings { get; } = mappings;
    public bool OwnersTableAvailable { get; } = ownersTableAvailable;
    public IReadOnlyList<string> SchemaWarnings { get; } = schemaWarnings;
}

internal sealed record RemoteProductRow(
    int Id,
    string Name,
    string? Vendor,
    string? Version,
    string? LifecycleStatus,
    string? Description,
    string? Notes,
    DateTime CreatedUtc,
    DateTime UpdatedUtc);

internal sealed record RemoteMappingRow(
    int Id,
    int ProductCatalogItemId,
    string ProductName,
    string? DomainCode,
    string? DomainName,
    string? DomainSourceTitle,
    string? CapabilityCode,
    string? CapabilityName,
    string? CapabilitySourceTitle,
    string? ComponentCode,
    string? ComponentTechnologyCode,
    string? ComponentName,
    string? ComponentSourceTitle,
    int? MappingStatusValue,
    string? MappingRationale,
    DateTime? LastReviewedUtc,
    DateTime CreatedUtc,
    DateTime UpdatedUtc);

internal sealed class RemoteSqlSchemaValidationResult
{
    public bool IsSuccess => Errors.Count == 0;
    public IReadOnlyDictionary<string, string> TableSchemas { get; init; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    public bool OwnersTableAvailable { get; init; }
    public IReadOnlyList<string> Errors { get; init; } = [];
    public IReadOnlyList<string> Warnings { get; init; } = [];

    public static RemoteSqlSchemaValidationResult Success(
        IReadOnlyDictionary<string, string> tableSchemas,
        bool ownersTableAvailable,
        IReadOnlyList<string> warnings) =>
        new()
        {
            TableSchemas = tableSchemas,
            OwnersTableAvailable = ownersTableAvailable,
            Warnings = warnings
        };

    public static RemoteSqlSchemaValidationResult Failure(IReadOnlyList<string> errors, IReadOnlyList<string>? warnings = null) =>
        new()
        {
            Errors = errors,
            Warnings = warnings ?? []
        };
}

internal sealed class ResolvedConnectionInput
{
    private ResolvedConnectionInput()
    {
    }

    public bool IsValid { get; init; }
    public string ServerName { get; init; } = string.Empty;
    public int Port { get; init; } = AppSettingDefaults.RemoteSqlImportPort;
    public string DatabaseName { get; init; } = string.Empty;
    public bool Encrypt { get; init; }
    public bool TrustServerCertificate { get; init; }
    public bool UseIntegratedSecurity { get; init; }
    public string EffectiveConnectionString { get; init; } = string.Empty;
    public string? EffectiveUserName { get; init; }
    public string? EffectivePassword { get; init; }
    public int ScheduleHours { get; init; }
    public bool ClearStoredCredentials { get; init; }
    public string? NewlySavedUserName { get; init; }
    public string? NewlySavedPassword { get; init; }
    public bool UserNameChanged { get; init; }
    public bool PasswordChanged { get; init; }
    public IReadOnlyList<string> Errors { get; init; } = [];

    public string CredentialStorageMode =>
        UseIntegratedSecurity || ClearStoredCredentials
            ? "integrated security only"
            : !string.IsNullOrWhiteSpace(EffectiveUserName) && !string.IsNullOrWhiteSpace(EffectivePassword)
                ? "stored SQL credentials"
                : "no credentials stored";

    public string ConnectionSecurityMode =>
        $"encrypt={(Encrypt ? "on" : "off")}, trustServerCertificate={(TrustServerCertificate ? "on" : "off")}";

    public static ResolvedConnectionInput Invalid(IReadOnlyList<string> errors) =>
        new()
        {
            Errors = errors
        };

    public static ResolvedConnectionInput Valid(
        string serverName,
        int port,
        string databaseName,
        bool encrypt,
        bool trustServerCertificate,
        bool useIntegratedSecurity,
        string? effectiveUserName,
        string? effectivePassword,
        string effectiveConnectionString,
        int scheduleHours,
        bool clearStoredCredentials,
        string? newlySavedUserName,
        string? newlySavedPassword,
        bool userNameChanged,
        bool passwordChanged) =>
        new()
        {
            IsValid = true,
            ServerName = serverName,
            Port = port,
            DatabaseName = databaseName,
            Encrypt = encrypt,
            TrustServerCertificate = trustServerCertificate,
            UseIntegratedSecurity = useIntegratedSecurity,
            EffectiveConnectionString = effectiveConnectionString,
            EffectiveUserName = effectiveUserName,
            EffectivePassword = effectivePassword,
            ScheduleHours = scheduleHours,
            ClearStoredCredentials = clearStoredCredentials,
            NewlySavedUserName = newlySavedUserName,
            NewlySavedPassword = newlySavedPassword,
            UserNameChanged = userNameChanged,
            PasswordChanged = passwordChanged
        };
}

internal enum RemoteSqlImportTrigger
{
    Manual,
    Scheduled
}

public sealed partial class RemoteSqlImportService
{
    private async Task<RemoteSqlImportSummary> ApplyImportSnapshotAsync(RemoteSqlSnapshot snapshot, CancellationToken cancellationToken)
    {
        var nowUtc = DateTime.UtcNow;
        var summary = new RemoteSqlImportSummary
        {
            RemoteProductsRead = snapshot.Products.Count,
            RemoteMappingsRead = snapshot.Mappings.Count
        };
        summary.Warnings.AddRange(snapshot.SchemaWarnings);

        var localDomains = await dbContext.TrmDomains
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        var localCapabilities = await dbContext.TrmCapabilities
            .AsNoTracking()
            .Include(x => x.ParentDomain)
            .ToListAsync(cancellationToken);
        var localComponents = await dbContext.TrmComponents
            .AsNoTracking()
            .Include(x => x.ParentCapability)
            .ThenInclude(x => x!.ParentDomain)
            .ToListAsync(cancellationToken);

        var existingProducts = await dbContext.ProductCatalogItems
            .Include(x => x.Owners)
            .OrderBy(x => x.Id)
            .ToListAsync(cancellationToken);
        var productsByName = existingProducts
            .GroupBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        foreach (var remoteProduct in snapshot.Products)
        {
            if (!productsByName.TryGetValue(remoteProduct.Name, out var localProduct))
            {
                localProduct = new ProductCatalogItem
                {
                    Name = TrimToLength(remoteProduct.Name, 200) ?? string.Empty,
                    Vendor = TrimToLength(remoteProduct.Vendor, 120),
                    Version = TrimToLength(remoteProduct.Version, 80),
                    LifecycleStatus = TrimToLength(remoteProduct.LifecycleStatus, 80),
                    Description = TrimToLength(remoteProduct.Description, 2000),
                    Notes = TrimToLength(remoteProduct.Notes, 4000),
                    CreatedUtc = remoteProduct.CreatedUtc == default ? nowUtc : remoteProduct.CreatedUtc,
                    UpdatedUtc = remoteProduct.UpdatedUtc == default ? nowUtc : remoteProduct.UpdatedUtc
                };

                dbContext.ProductCatalogItems.Add(localProduct);
                productsByName[remoteProduct.Name] = localProduct;
                summary.ProductsAdded++;
            }
            else
            {
                var changed = false;
                changed |= ApplyStringChange(remoteProduct.Name, 200, value => localProduct.Name = value ?? localProduct.Name, localProduct.Name);
                changed |= ApplyStringChange(remoteProduct.Vendor, 120, value => localProduct.Vendor = value, localProduct.Vendor);
                changed |= ApplyStringChange(remoteProduct.Version, 80, value => localProduct.Version = value, localProduct.Version);
                changed |= ApplyStringChange(remoteProduct.LifecycleStatus, 80, value => localProduct.LifecycleStatus = value, localProduct.LifecycleStatus);
                changed |= ApplyStringChange(remoteProduct.Description, 2000, value => localProduct.Description = value, localProduct.Description);
                changed |= ApplyStringChange(remoteProduct.Notes, 4000, value => localProduct.Notes = value, localProduct.Notes);

                if (localProduct.IsDeleted || localProduct.DeletedUtc.HasValue || !string.IsNullOrWhiteSpace(localProduct.DeletedReason))
                {
                    localProduct.IsDeleted = false;
                    localProduct.DeletedUtc = null;
                    localProduct.DeletedReason = null;
                    changed = true;
                }

                if (changed)
                {
                    localProduct.UpdatedUtc = nowUtc;
                    summary.ProductsUpdated++;
                }
                else
                {
                    summary.ProductsMatched++;
                }
            }

            if (snapshot.OwnersTableAvailable && snapshot.OwnersByProductId.TryGetValue(remoteProduct.Id, out var ownerValues))
            {
                summary.OwnerSetsUpdated += SynchronizeOwners(localProduct, ownerValues) ? 1 : 0;
            }
        }

        var existingMappings = await dbContext.ProductMappings
            .Include(x => x.ProductCatalogItem)
            .ToListAsync(cancellationToken);
        var mappingsByKey = existingMappings
            .Where(x => x.ProductCatalogItem is not null)
            .ToDictionary(
                x => BuildMappingKey(x.ProductCatalogItem!.Name, x.TrmDomainId, x.TrmCapabilityId, x.TrmComponentId),
                x => x,
                StringComparer.OrdinalIgnoreCase);

        foreach (var remoteMapping in snapshot.Mappings)
        {
            if (string.IsNullOrWhiteSpace(remoteMapping.ProductName))
            {
                summary.MappingsSkipped++;
                summary.Warnings.Add($"Remote mapping #{remoteMapping.Id} has no product name.");
                continue;
            }

            if (!productsByName.TryGetValue(remoteMapping.ProductName, out var localProduct))
            {
                summary.MappingsSkipped++;
                summary.Warnings.Add($"Remote mapping #{remoteMapping.Id} references product '{remoteMapping.ProductName}', which could not be loaded locally.");
                continue;
            }

            if (!TryResolveLocalMapping(remoteMapping, localDomains, localCapabilities, localComponents, out var resolvedMapping, out var resolutionMessage))
            {
                summary.MappingsSkipped++;
                summary.Warnings.Add($"Remote mapping #{remoteMapping.Id} for '{remoteMapping.ProductName}' was skipped. {resolutionMessage}");
                continue;
            }

            var mappingKey = BuildMappingKey(localProduct.Name, resolvedMapping.Domain.Id, resolvedMapping.Capability.Id, resolvedMapping.Component.Id);
            if (!mappingsByKey.TryGetValue(mappingKey, out var localMapping))
            {
                localMapping = new ProductMapping
                {
                    ProductCatalogItem = localProduct,
                    TrmDomainId = resolvedMapping.Domain.Id,
                    TrmCapabilityId = resolvedMapping.Capability.Id,
                    TrmComponentId = resolvedMapping.Component.Id,
                    MappingStatus = ResolveMappingStatus(remoteMapping.MappingStatusValue),
                    MappingRationale = TrimToLength(remoteMapping.MappingRationale, 4000),
                    LastReviewedUtc = remoteMapping.LastReviewedUtc,
                    CreatedUtc = remoteMapping.CreatedUtc == default ? nowUtc : remoteMapping.CreatedUtc,
                    UpdatedUtc = remoteMapping.UpdatedUtc == default ? nowUtc : remoteMapping.UpdatedUtc
                };

                dbContext.ProductMappings.Add(localMapping);
                mappingsByKey[mappingKey] = localMapping;
                summary.MappingsAdded++;
                continue;
            }

            var changed = false;
            var resolvedStatus = ResolveMappingStatus(remoteMapping.MappingStatusValue);
            if (localMapping.MappingStatus != resolvedStatus)
            {
                localMapping.MappingStatus = resolvedStatus;
                changed = true;
            }

            var trimmedRationale = TrimToLength(remoteMapping.MappingRationale, 4000);
            if (!string.Equals(localMapping.MappingRationale, trimmedRationale, StringComparison.Ordinal))
            {
                localMapping.MappingRationale = trimmedRationale;
                changed = true;
            }

            if (localMapping.LastReviewedUtc != remoteMapping.LastReviewedUtc)
            {
                localMapping.LastReviewedUtc = remoteMapping.LastReviewedUtc;
                changed = true;
            }

            if (changed)
            {
                localMapping.UpdatedUtc = nowUtc;
                summary.MappingsUpdated++;
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return summary;
    }

    private static bool TryResolveLocalMapping(
        RemoteMappingRow remoteMapping,
        IReadOnlyCollection<TrmDomain> domains,
        IReadOnlyCollection<TrmCapability> capabilities,
        IReadOnlyCollection<TrmComponent> components,
        [NotNullWhen(true)] out ResolvedRelationshipMapping? resolvedMapping,
        out string resolutionMessage)
    {
        resolvedMapping = default;
        resolutionMessage = string.Empty;

        var component = ResolveComponent(remoteMapping, components);
        if (component is null || component.ParentCapabilityId is null)
        {
            resolutionMessage = "Component could not be matched to a local technology component.";
            return false;
        }

        var capability = capabilities.FirstOrDefault(x => x.Id == component.ParentCapabilityId);
        if (capability is null || capability.ParentDomainId is null)
        {
            resolutionMessage = "Capability could not be resolved from the matched component.";
            return false;
        }

        var domain = domains.FirstOrDefault(x => x.Id == capability.ParentDomainId);
        if (domain is null)
        {
            resolutionMessage = "Domain could not be resolved from the matched component hierarchy.";
            return false;
        }

        if (!MatchesHierarchy(remoteMapping, domain, capability, component))
        {
            resolutionMessage = "Remote mapping hierarchy does not match the local HERM hierarchy.";
            return false;
        }

        resolvedMapping = new ResolvedRelationshipMapping(domain, capability, component);
        resolutionMessage = $"Resolved to {domain.Code} {domain.Name} / {capability.Code} {capability.Name} / {component.DisplayLabel}.";
        return true;
    }

    private static TrmComponent? ResolveComponent(RemoteMappingRow remoteMapping, IReadOnlyCollection<TrmComponent> components)
    {
        if (!string.IsNullOrWhiteSpace(remoteMapping.ComponentTechnologyCode))
        {
            var technologyCodeMatch = components.FirstOrDefault(x =>
                string.Equals(x.TechnologyComponentCode, remoteMapping.ComponentTechnologyCode, StringComparison.OrdinalIgnoreCase));
            if (technologyCodeMatch is not null)
            {
                return technologyCodeMatch;
            }
        }

        if (!string.IsNullOrWhiteSpace(remoteMapping.ComponentCode))
        {
            var codeMatch = components.FirstOrDefault(x =>
                string.Equals(x.Code, remoteMapping.ComponentCode, StringComparison.OrdinalIgnoreCase));
            if (codeMatch is not null)
            {
                return codeMatch;
            }
        }

        return components
            .Where(x =>
                MatchesValue(remoteMapping.ComponentName, x.Name, x.SourceTitle) ||
                MatchesValue(remoteMapping.ComponentSourceTitle, x.Name, x.SourceTitle))
            .OrderBy(x => x.Id)
            .FirstOrDefault();
    }

    private static bool MatchesHierarchy(RemoteMappingRow remoteMapping, TrmDomain domain, TrmCapability capability, TrmComponent component) =>
        MatchesValue(remoteMapping.DomainCode, domain.Code, null) &&
        MatchesValue(remoteMapping.DomainName, domain.Name, domain.SourceTitle) &&
        MatchesValue(remoteMapping.CapabilityCode, capability.Code, null) &&
        MatchesValue(remoteMapping.CapabilityName, capability.Name, capability.SourceTitle) &&
        (string.IsNullOrWhiteSpace(remoteMapping.ComponentCode) || string.Equals(component.Code, remoteMapping.ComponentCode, StringComparison.OrdinalIgnoreCase)) &&
        (string.IsNullOrWhiteSpace(remoteMapping.ComponentTechnologyCode) || string.Equals(component.TechnologyComponentCode, remoteMapping.ComponentTechnologyCode, StringComparison.OrdinalIgnoreCase)) &&
        MatchesValue(remoteMapping.ComponentName, component.Name, component.SourceTitle);

    private static bool MatchesValue(string? expectedValue, string? primaryValue, string? secondaryValue)
    {
        if (string.IsNullOrWhiteSpace(expectedValue))
        {
            return true;
        }

        return string.Equals(expectedValue, primaryValue, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(expectedValue, secondaryValue, StringComparison.OrdinalIgnoreCase);
    }

    private static bool SynchronizeOwners(ProductCatalogItem product, IReadOnlyList<string> remoteOwners)
    {
        var desiredOwners = remoteOwners
            .Select(owner => TrimToLength(owner, 120))
            .Where(owner => !string.IsNullOrWhiteSpace(owner))
            .Select(owner => owner!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(owner => owner, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var existingOwners = product.Owners.Select(owner => owner.OwnerValue).ToList();
        var changed = false;

        foreach (var owner in product.Owners.Where(owner => desiredOwners.All(value => !string.Equals(value, owner.OwnerValue, StringComparison.OrdinalIgnoreCase))).ToList())
        {
            product.Owners.Remove(owner);
            changed = true;
        }

        foreach (var desiredOwner in desiredOwners.Where(value => existingOwners.All(existingOwner => !string.Equals(existingOwner, value, StringComparison.OrdinalIgnoreCase))))
        {
            product.Owners.Add(new ProductCatalogItemOwner
            {
                OwnerValue = desiredOwner
            });
            changed = true;
        }

        return changed;
    }
}

public sealed partial class RemoteSqlImportService
{
    private async Task<RemoteSqlSchemaValidationResult> ValidateRemoteSchemaAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        var tablesByName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var columnsByTable = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        await using (var command = connection.CreateCommand())
        {
            command.CommandText =
                """
                SELECT s.name AS SchemaName, t.name AS TableName, c.name AS ColumnName
                FROM sys.tables t
                INNER JOIN sys.schemas s ON s.schema_id = t.schema_id
                INNER JOIN sys.columns c ON c.object_id = t.object_id
                WHERE t.name IN (N'ProductCatalogItems', N'ProductMappings', N'TrmDomains', N'TrmCapabilities', N'TrmComponents', N'ProductCatalogItemOwners')
                ORDER BY t.name, c.column_id
                """;

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var schemaName = reader.GetString(0);
                var tableName = reader.GetString(1);
                var columnName = reader.GetString(2);

                if (tablesByName.TryGetValue(tableName, out var existingSchemaName) &&
                    !string.Equals(existingSchemaName, schemaName, StringComparison.OrdinalIgnoreCase))
                {
                    return RemoteSqlSchemaValidationResult.Failure([$"Multiple schemas contain the table '{tableName}'. Use a database with a single HERM schema."]);
                }

                tablesByName[tableName] = schemaName;
                if (!columnsByTable.TryGetValue(tableName, out var columns))
                {
                    columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    columnsByTable[tableName] = columns;
                }

                columns.Add(columnName);
            }
        }

        var errors = new List<string>();
        var warnings = new List<string>();
        var resolvedSchemas = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var requiredTable in RequiredSchema)
        {
            if (!tablesByName.TryGetValue(requiredTable.Key, out var schemaName))
            {
                errors.Add($"Table '{requiredTable.Key}' is missing.");
                continue;
            }

            resolvedSchemas[requiredTable.Key] = schemaName;
            var columns = columnsByTable[requiredTable.Key];
            foreach (var requiredColumn in requiredTable.Value)
            {
                if (!columns.Contains(requiredColumn))
                {
                    errors.Add($"Column '{requiredTable.Key}.{requiredColumn}' is missing.");
                }
            }
        }

        var ownersTableAvailable = false;
        if (tablesByName.TryGetValue("ProductCatalogItemOwners", out var ownersSchema))
        {
            ownersTableAvailable = true;
            resolvedSchemas["ProductCatalogItemOwners"] = ownersSchema;
            var ownersColumns = columnsByTable["ProductCatalogItemOwners"];
            foreach (var requiredColumn in OptionalSchema["ProductCatalogItemOwners"])
            {
                if (!ownersColumns.Contains(requiredColumn))
                {
                    ownersTableAvailable = false;
                    warnings.Add($"Owners table is present, but column 'ProductCatalogItemOwners.{requiredColumn}' is missing. Owner values will not be imported.");
                }
            }
        }
        else
        {
            warnings.Add("Owners table was not found. Product owners will not be imported.");
        }

        return errors.Count > 0
            ? RemoteSqlSchemaValidationResult.Failure(errors, warnings)
            : RemoteSqlSchemaValidationResult.Success(resolvedSchemas, ownersTableAvailable, warnings);
    }

    private async Task<RemoteSqlCounts> ReadRemoteCountsAsync(
        SqlConnection connection,
        IReadOnlyDictionary<string, string> tableSchemas,
        CancellationToken cancellationToken)
    {
        var productCount = await ExecuteScalarIntAsync(
            connection,
            $"SELECT COUNT(*) FROM {BuildQualifiedTableName(tableSchemas, "ProductCatalogItems")} WHERE ISNULL([IsDeleted], 0) = 0;",
            cancellationToken);
        var mappingCount = await ExecuteScalarIntAsync(
            connection,
            $"""
             SELECT COUNT(*)
             FROM {BuildQualifiedTableName(tableSchemas, "ProductMappings")} pm
             INNER JOIN {BuildQualifiedTableName(tableSchemas, "ProductCatalogItems")} p
                 ON p.[Id] = pm.[ProductCatalogItemId]
             WHERE ISNULL(p.[IsDeleted], 0) = 0;
             """,
            cancellationToken);

        return new RemoteSqlCounts(productCount, mappingCount);
    }

    private async Task<RemoteSqlSnapshot> ReadRemoteSnapshotAsync(
        SqlConnection connection,
        RemoteSqlSchemaValidationResult schemaValidation,
        CancellationToken cancellationToken)
    {
        var products = await ReadRemoteProductsAsync(connection, schemaValidation.TableSchemas, cancellationToken);
        var ownersByProductId = schemaValidation.OwnersTableAvailable
            ? await ReadRemoteOwnersAsync(connection, schemaValidation.TableSchemas, cancellationToken)
            : new Dictionary<int, IReadOnlyList<string>>();
        var mappings = await ReadRemoteMappingsAsync(connection, schemaValidation.TableSchemas, cancellationToken);

        return new RemoteSqlSnapshot(products, ownersByProductId, mappings, schemaValidation.OwnersTableAvailable, schemaValidation.Warnings);
    }

    private async Task<List<RemoteProductRow>> ReadRemoteProductsAsync(
        SqlConnection connection,
        IReadOnlyDictionary<string, string> tableSchemas,
        CancellationToken cancellationToken)
    {
        var products = new List<RemoteProductRow>();
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
             SELECT [Id], [Name], [Vendor], [Version], [LifecycleStatus], [Description], [Notes], [CreatedUtc], [UpdatedUtc]
             FROM {BuildQualifiedTableName(tableSchemas, "ProductCatalogItems")}
             WHERE ISNULL([IsDeleted], 0) = 0
             ORDER BY [Id];
             """;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var productName = ReadTrimmedString(reader, 1);
            if (string.IsNullOrWhiteSpace(productName))
            {
                continue;
            }

            products.Add(new RemoteProductRow(
                reader.GetInt32(0),
                productName,
                ReadTrimmedString(reader, 2),
                ReadTrimmedString(reader, 3),
                ReadTrimmedString(reader, 4),
                ReadTrimmedString(reader, 5),
                ReadTrimmedString(reader, 6),
                ReadDateTime(reader, 7),
                ReadDateTime(reader, 8)));
        }

        return products;
    }

    private async Task<Dictionary<int, IReadOnlyList<string>>> ReadRemoteOwnersAsync(
        SqlConnection connection,
        IReadOnlyDictionary<string, string> tableSchemas,
        CancellationToken cancellationToken)
    {
        var ownersByProductId = new Dictionary<int, List<string>>();
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
             SELECT [ProductCatalogItemId], [OwnerValue]
             FROM {BuildQualifiedTableName(tableSchemas, "ProductCatalogItemOwners")}
             WHERE NULLIF(LTRIM(RTRIM([OwnerValue])), N'') IS NOT NULL
             ORDER BY [ProductCatalogItemId], [OwnerValue];
             """;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var ownerValue = ReadTrimmedString(reader, 1);
            if (string.IsNullOrWhiteSpace(ownerValue))
            {
                continue;
            }

            var productId = reader.GetInt32(0);
            if (!ownersByProductId.TryGetValue(productId, out var owners))
            {
                owners = [];
                ownersByProductId[productId] = owners;
            }

            if (!owners.Contains(ownerValue, StringComparer.OrdinalIgnoreCase))
            {
                owners.Add(ownerValue);
            }
        }

        return ownersByProductId.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<string>)pair.Value.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList());
    }

    private async Task<List<RemoteMappingRow>> ReadRemoteMappingsAsync(
        SqlConnection connection,
        IReadOnlyDictionary<string, string> tableSchemas,
        CancellationToken cancellationToken)
    {
        var mappings = new List<RemoteMappingRow>();
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
             SELECT
                 pm.[Id],
                 pm.[ProductCatalogItemId],
                 p.[Name],
                 d.[Code],
                 d.[Name],
                 d.[SourceTitle],
                 c.[Code],
                 c.[Name],
                 c.[SourceTitle],
                 tc.[Code],
                 tc.[TechnologyComponentCode],
                 tc.[Name],
                 tc.[SourceTitle],
                 pm.[MappingStatus],
                 pm.[MappingRationale],
                 pm.[LastReviewedUtc],
                 pm.[CreatedUtc],
                 pm.[UpdatedUtc]
             FROM {BuildQualifiedTableName(tableSchemas, "ProductMappings")} pm
             INNER JOIN {BuildQualifiedTableName(tableSchemas, "ProductCatalogItems")} p
                 ON p.[Id] = pm.[ProductCatalogItemId]
             LEFT JOIN {BuildQualifiedTableName(tableSchemas, "TrmDomains")} d
                 ON d.[Id] = pm.[TrmDomainId]
             LEFT JOIN {BuildQualifiedTableName(tableSchemas, "TrmCapabilities")} c
                 ON c.[Id] = pm.[TrmCapabilityId]
             LEFT JOIN {BuildQualifiedTableName(tableSchemas, "TrmComponents")} tc
                 ON tc.[Id] = pm.[TrmComponentId]
             WHERE ISNULL(p.[IsDeleted], 0) = 0;
             """;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            mappings.Add(new RemoteMappingRow(
                reader.GetInt32(0),
                reader.GetInt32(1),
                ReadTrimmedString(reader, 2) ?? string.Empty,
                ReadTrimmedString(reader, 3),
                ReadTrimmedString(reader, 4),
                ReadTrimmedString(reader, 5),
                ReadTrimmedString(reader, 6),
                ReadTrimmedString(reader, 7),
                ReadTrimmedString(reader, 8),
                ReadTrimmedString(reader, 9),
                ReadTrimmedString(reader, 10),
                ReadTrimmedString(reader, 11),
                ReadTrimmedString(reader, 12),
                ReadNullableInt(reader, 13),
                ReadTrimmedString(reader, 14),
                ReadNullableDateTime(reader, 15),
                ReadDateTime(reader, 16),
                ReadDateTime(reader, 17)));
        }

        return mappings;
    }
}
