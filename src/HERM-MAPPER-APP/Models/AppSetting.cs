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
    public const string RemoteSqlImportConnectionString = "RemoteSqlImport.ConnectionString";
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
    public const int RemoteSqlImportScheduleHours = 0;
}
