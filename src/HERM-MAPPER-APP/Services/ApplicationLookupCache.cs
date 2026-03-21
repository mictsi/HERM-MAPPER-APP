using System.Collections.Concurrent;
using Microsoft.Extensions.Caching.Memory;

namespace HERMMapperApp.Services;

public sealed class ApplicationLookupCache(IMemoryCache memoryCache)
{
    private const string RemoteSqlImportSettingsCacheKey = "lookup:remote-sql-import:settings";
    private static readonly MemoryCacheEntryOptions AppSettingCacheOptions = new()
    {
        AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(15)
    };
    private static readonly MemoryCacheEntryOptions ConfigurableFieldOptionsCacheOptions = new()
    {
        AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(15)
    };
    private static readonly MemoryCacheEntryOptions RemoteSqlImportSettingsCacheOptions = new()
    {
        AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5)
    };

    private readonly ConcurrentDictionary<string, byte> trackedAppSettingKeys = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, byte> trackedConfigurableFieldNames = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, SemaphoreSlim> keyLocks = new(StringComparer.Ordinal);

    public async Task<string?> GetOrCreateAppSettingAsync(
        string key,
        Func<CancellationToken, Task<string?>> factory,
        CancellationToken cancellationToken = default)
    {
        trackedAppSettingKeys.TryAdd(key, 0);

        var cached = await GetOrCreateAsync(
            BuildAppSettingCacheKey(key),
            async token => new NullableStringCacheValue(await factory(token)),
            AppSettingCacheOptions,
            cancellationToken);

        return cached.Value;
    }

    public async Task<string?> RefreshAppSettingAsync(
        string key,
        Func<CancellationToken, Task<string?>> factory,
        CancellationToken cancellationToken = default)
    {
        trackedAppSettingKeys.TryAdd(key, 0);

        var refreshed = new NullableStringCacheValue(await factory(cancellationToken));
        memoryCache.Set(BuildAppSettingCacheKey(key), refreshed, AppSettingCacheOptions);
        return refreshed.Value;
    }

    public void InvalidateAppSetting(string key)
    {
        trackedAppSettingKeys.TryAdd(key, 0);
        memoryCache.Remove(BuildAppSettingCacheKey(key));
        InvalidateRemoteSqlImportSettings();
    }

    public async Task<IReadOnlyList<Models.ConfigurableFieldOption>> GetOrCreateConfigurableFieldOptionsAsync(
        string fieldName,
        Func<CancellationToken, Task<IReadOnlyList<Models.ConfigurableFieldOption>>> factory,
        CancellationToken cancellationToken = default)
    {
        trackedConfigurableFieldNames.TryAdd(fieldName, 0);

        return await GetOrCreateAsync(
            BuildConfigurableFieldOptionsCacheKey(fieldName),
            factory,
            ConfigurableFieldOptionsCacheOptions,
            cancellationToken);
    }

    public async Task<IReadOnlyList<Models.ConfigurableFieldOption>> RefreshConfigurableFieldOptionsAsync(
        string fieldName,
        Func<CancellationToken, Task<IReadOnlyList<Models.ConfigurableFieldOption>>> factory,
        CancellationToken cancellationToken = default)
    {
        trackedConfigurableFieldNames.TryAdd(fieldName, 0);

        var refreshed = await factory(cancellationToken);
        memoryCache.Set(BuildConfigurableFieldOptionsCacheKey(fieldName), refreshed, ConfigurableFieldOptionsCacheOptions);
        return refreshed;
    }

    public void InvalidateConfigurableFieldOptions(string fieldName)
    {
        trackedConfigurableFieldNames.TryAdd(fieldName, 0);
        memoryCache.Remove(BuildConfigurableFieldOptionsCacheKey(fieldName));
    }

    public async Task<RemoteSqlImportSettingsSnapshot> GetOrCreateRemoteSqlImportSettingsAsync(
        Func<CancellationToken, Task<RemoteSqlImportSettingsSnapshot>> factory,
        CancellationToken cancellationToken = default)
    {
        return await GetOrCreateAsync(
            RemoteSqlImportSettingsCacheKey,
            factory,
            RemoteSqlImportSettingsCacheOptions,
            cancellationToken);
    }

    public async Task<RemoteSqlImportSettingsSnapshot> RefreshRemoteSqlImportSettingsAsync(
        Func<CancellationToken, Task<RemoteSqlImportSettingsSnapshot>> factory,
        CancellationToken cancellationToken = default)
    {
        var refreshed = await factory(cancellationToken);
        memoryCache.Set(RemoteSqlImportSettingsCacheKey, refreshed, RemoteSqlImportSettingsCacheOptions);
        return refreshed;
    }

    public void InvalidateRemoteSqlImportSettings()
    {
        memoryCache.Remove(RemoteSqlImportSettingsCacheKey);
    }

    public IReadOnlyList<string> GetTrackedAppSettingKeys() => [.. trackedAppSettingKeys.Keys];

    public IReadOnlyList<string> GetTrackedConfigurableFieldNames() => [.. trackedConfigurableFieldNames.Keys];

    public bool HasRemoteSqlImportSettingsSnapshot() =>
        memoryCache.TryGetValue(RemoteSqlImportSettingsCacheKey, out _);

    private async Task<TValue> GetOrCreateAsync<TValue>(
        string cacheKey,
        Func<CancellationToken, Task<TValue>> factory,
        MemoryCacheEntryOptions options,
        CancellationToken cancellationToken)
    {
        if (memoryCache.TryGetValue<TValue>(cacheKey, out var cachedValue) && cachedValue is not null)
        {
            return cachedValue;
        }

        var gate = keyLocks.GetOrAdd(cacheKey, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);

        try
        {
            if (memoryCache.TryGetValue<TValue>(cacheKey, out cachedValue) && cachedValue is not null)
            {
                return cachedValue;
            }

            var createdValue = await factory(cancellationToken);
            memoryCache.Set(cacheKey, createdValue, options);
            return createdValue;
        }
        finally
        {
            gate.Release();
        }
    }

    private static string BuildAppSettingCacheKey(string key) => $"lookup:app-setting:{key}";

    private static string BuildConfigurableFieldOptionsCacheKey(string fieldName) => $"lookup:configurable-field:{fieldName}";

    private sealed record NullableStringCacheValue(string? Value);
}