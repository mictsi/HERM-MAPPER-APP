using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HERMMapperApp.Services;

public sealed class RemoteSqlImportHostedService(
    IServiceProvider serviceProvider,
    ILogger<RemoteSqlImportHostedService> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(5);
    private static readonly Action<ILogger, Exception?> LogScheduledImportPollingFailed =
        LoggerMessage.Define(
            LogLevel.Error,
            new EventId(1, nameof(RunScheduledImportAsync)),
            "Scheduled remote SQL import polling failed.");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RunScheduledImportAsync(stoppingToken);

        using var timer = new PeriodicTimer(PollInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await RunScheduledImportAsync(stoppingToken);
        }
    }

    private async Task RunScheduledImportAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = serviceProvider.CreateAsyncScope();
            var remoteSqlImportService = scope.ServiceProvider.GetRequiredService<RemoteSqlImportService>();
            await remoteSqlImportService.RunScheduledImportIfDueAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Expected during shutdown.
        }
        catch (Exception exception)
        {
            LogScheduledImportPollingFailed(logger, exception);
        }
    }
}
