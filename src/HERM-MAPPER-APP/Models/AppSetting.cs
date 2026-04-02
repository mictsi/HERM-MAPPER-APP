namespace HERMMapperApp.Models;

public sealed class AppSetting
{
    public int Id { get; set; }

    public string Key { get; set; } = string.Empty;

    public string Value { get; set; } = string.Empty;

    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
}

public static class AppSettingKeys
{
    public const string DisplayTimeZone = "DisplayTimeZone";
    public const string AiMappingEndpoint = "AiMapping.Endpoint";
    public const string AiMappingModel = "AiMapping.Model";
    public const string AiMappingApiKey = "AiMapping.ApiKey";
    public const string AiMappingIsEnabled = "AiMapping.IsEnabled";
    public const string AiMappingTimeoutSeconds = "AiMapping.TimeoutSeconds";
    public const string RemoteSqlImportConnectionString = "RemoteSqlImport.ConnectionString";
    public const string RemoteSqlImportIsEnabled = "RemoteSqlImport.IsEnabled";
    public const string RemoteSqlImportServerName = "RemoteSqlImport.ServerName";
    public const string RemoteSqlImportPort = "RemoteSqlImport.Port";
    public const string RemoteSqlImportDatabaseName = "RemoteSqlImport.DatabaseName";
    public const string RemoteSqlImportEncrypt = "RemoteSqlImport.Encrypt";
    public const string RemoteSqlImportTrustServerCertificate = "RemoteSqlImport.TrustServerCertificate";
    public const string RemoteSqlImportUseIntegratedSecurity = "RemoteSqlImport.UseIntegratedSecurity";
    public const string RemoteSqlImportUserName = "RemoteSqlImport.UserName";
    public const string RemoteSqlImportPassword = "RemoteSqlImport.Password";
    public const string RemoteSqlImportScheduleHours = "RemoteSqlImport.ScheduleHours";
    public const string RemoteSqlImportLastAttemptUtc = "RemoteSqlImport.LastAttemptUtc";
    public const string RemoteSqlImportLastSuccessUtc = "RemoteSqlImport.LastSuccessUtc";
    public const string RemoteSqlImportLastStatus = "RemoteSqlImport.LastStatus";
    public const string RemoteSqlImportLastMessage = "RemoteSqlImport.LastMessage";
}

public static class AppSettingDefaults
{
    public const string DisplayTimeZone = "UTC";
    public const string AiMappingEndpoint = "http://localhost:3000/api/chat/completions";
    public const string AiMappingModel = "gpt-oss:latest";
    public const bool AiMappingEnabled = false;
    public const int AiMappingTimeoutSeconds = 120;
    public const bool RemoteSqlImportEnabled = true;
    public const int RemoteSqlImportScheduleHours = 0;
    public const int RemoteSqlImportPort = 1433;
    public const bool RemoteSqlImportEncrypt = true;
    public const bool RemoteSqlImportTrustServerCertificate = false;
    public const bool RemoteSqlImportUseIntegratedSecurity = false;
}
