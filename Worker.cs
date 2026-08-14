using IpSync.Options;
using IpSync.Services;
using Microsoft.Extensions.Options;

namespace IpSync;

public sealed class Worker(
    IpSyncRunner ipSyncRunner,
    IOptions<IpSyncOptions> options,
    ILogger<Worker> logger) : BackgroundService
{
    private readonly IpSyncOptions options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RunOnceAsync(stoppingToken);

        var interval = TimeSpan.FromMinutes(Math.Max(1, options.IntervalMinutes));
        using var timer = new PeriodicTimer(interval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await timer.WaitForNextTickAsync(stoppingToken);
                await RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "IP sync failed. The service will try again at the next interval.");
            }
        }
    }

    private async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        await ipSyncRunner.RunOnceAsync(cancellationToken);
    }
}
