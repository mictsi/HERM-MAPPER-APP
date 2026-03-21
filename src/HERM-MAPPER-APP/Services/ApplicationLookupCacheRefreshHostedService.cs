using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using HERMMapperApp.Configuration;

namespace HERMMapperApp.Services;

public sealed class ApplicationLookupCacheRefreshHostedService(
    IServiceProvider serviceProvider,
    LookupCacheRefreshOptions refreshOptions,
    ILogger<ApplicationLookupCacheRefreshHostedService> logger) : BackgroundService
{
    private static readonly Action<ILogger, Exception?> LogCacheRefreshCancelled =
        LoggerMessage.Define(
            LogLevel.Debug,
            new EventId(2, nameof(RefreshCacheAsync)),
            "Refreshing application lookup cache was cancelled.");
    private static readonly Action<ILogger, Exception?> LogCacheRefreshFailed =
        LoggerMessage.Define(
            LogLevel.Warning,
            new EventId(1, nameof(RefreshCacheAsync)),
            "Refreshing application lookup cache failed.");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(refreshOptions.RefreshInterval);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await RefreshCacheAsync(stoppingToken);
        }
    }

    private async Task RefreshCacheAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = serviceProvider.CreateAsyncScope();

            var lookupCache = scope.ServiceProvider.GetRequiredService<ApplicationLookupCache>();
            var appSettingsService = scope.ServiceProvider.GetRequiredService<AppSettingsService>();
            var configurableFieldService = scope.ServiceProvider.GetRequiredService<ConfigurableFieldService>();

            foreach (var key in lookupCache.GetTrackedAppSettingKeys())
            {
                await appSettingsService.RefreshCachedValueAsync(key, cancellationToken);
            }

            foreach (var fieldName in lookupCache.GetTrackedConfigurableFieldNames())
            {
                await configurableFieldService.RefreshCachedOptionsAsync(fieldName, cancellationToken);
            }

            if (lookupCache.HasRemoteSqlImportSettingsSnapshot())
            {
                var remoteSqlImportService = scope.ServiceProvider.GetRequiredService<RemoteSqlImportService>();
                await remoteSqlImportService.RefreshCachedSettingsAsync(cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            LogCacheRefreshCancelled(logger, null);
        }
        catch (Exception exception)
        {
            LogCacheRefreshFailed(logger, exception);
        }
    }
}