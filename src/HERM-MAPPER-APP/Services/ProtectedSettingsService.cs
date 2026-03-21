using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;

namespace HERMMapperApp.Services;

public sealed class ProtectedSettingsService(
    IDataProtectionProvider dataProtectionProvider,
    AppSettingsService appSettingsService,
    ILogger<ProtectedSettingsService> logger)
{
    private const string ProtectedValuePrefix = "dp:";
    private static readonly Action<ILogger, string, Exception?> LogFailedToUnprotectSetting =
        LoggerMessage.Define<string>(
            LogLevel.Warning,
            new EventId(1, nameof(GetValueAsync)),
            "Failed to unprotect app setting {AppSettingKey}.");
    private readonly IDataProtector protector = dataProtectionProvider.CreateProtector("HERMMapperApp.ProtectedSettings");

    public async Task<string?> GetValueAsync(string key, CancellationToken cancellationToken = default)
    {
        var storedValue = await appSettingsService.GetNullableValueAsync(key, cancellationToken);
        if (string.IsNullOrWhiteSpace(storedValue))
        {
            return null;
        }

        if (!storedValue.StartsWith(ProtectedValuePrefix, StringComparison.Ordinal))
        {
            return storedValue;
        }

        try
        {
            return protector.Unprotect(storedValue[ProtectedValuePrefix.Length..]);
        }
        catch (Exception exception)
        {
            LogFailedToUnprotectSetting(logger, key, exception);
            return null;
        }
    }

    public async Task SetValueAsync(string key, string? value, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            await appSettingsService.DeleteValueAsync(key, cancellationToken);
            return;
        }

        await appSettingsService.SetValueAsync(
            key,
            $"{ProtectedValuePrefix}{protector.Protect(value)}",
            cancellationToken);
    }
}
