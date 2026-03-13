namespace HERM_MAPPER_APP.Models;

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
}

public static class AppSettingDefaults
{
    public const string DisplayTimeZone = "UTC";
}