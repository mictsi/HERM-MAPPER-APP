using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HERMMapperApp.Services;

public sealed class RemoteSqlImportHostedService(
    IServiceProvider serviceProvider,
    ILogger<RemoteSqlImportHostedService> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(5);

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
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Scheduled remote SQL import polling failed.");
        }
    }
}
