using System.Globalization;
using HERMMapperApp.Models;

namespace HERMMapperApp.Services;

public sealed class ConfiguredTimeZoneService(AppSettingsService appSettingsService)
{
    private TimeZoneInfo? cachedTimeZone;
    private string? cachedTimeZoneId;

    public async Task<TimeZoneInfo> GetTimeZoneAsync(CancellationToken cancellationToken = default)
    {
        if (cachedTimeZone is not null)
        {
            return cachedTimeZone;
        }

        var configuredId = await appSettingsService.GetValueAsync(
            AppSettingKeys.DisplayTimeZone,
            AppSettingDefaults.DisplayTimeZone,
            cancellationToken);

        cachedTimeZone = ResolveTimeZone(configuredId);
        cachedTimeZoneId = cachedTimeZone.Id;
        return cachedTimeZone;
    }

    public async Task<string> GetTimeZoneIdAsync(CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(cachedTimeZoneId))
        {
            return cachedTimeZoneId;
        }

        var timeZone = await GetTimeZoneAsync(cancellationToken);
        return timeZone.Id;
    }

    public async Task<string> FormatUtcAsync(DateTime value, string format = "yyyy-MM-dd HH:mm", CancellationToken cancellationToken = default)
    {
        var timeZone = await GetTimeZoneAsync(cancellationToken);
        var utcValue = EnsureUtc(value);
        return TimeZoneInfo.ConvertTimeFromUtc(utcValue, timeZone).ToString(format, CultureInfo.InvariantCulture);
    }

    public async Task<string?> FormatUtcAsync(DateTime? value, string format = "yyyy-MM-dd HH:mm", CancellationToken cancellationToken = default)
    {
        if (!value.HasValue)
        {
            return null;
        }

        return await FormatUtcAsync(value.Value, format, cancellationToken);
    }

    public static TimeZoneInfo ResolveTimeZone(string? configuredId)
    {
        if (string.IsNullOrWhiteSpace(configuredId))
        {
            return TimeZoneInfo.Utc;
        }

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(configuredId);
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.Utc;
        }
        catch (InvalidTimeZoneException)
        {
            return TimeZoneInfo.Utc;
        }
    }

    private static DateTime EnsureUtc(DateTime value) =>
        value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };
}