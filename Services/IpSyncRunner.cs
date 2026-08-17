using IpSync.Options;
using Microsoft.Extensions.Options;

namespace IpSync.Services;

public sealed class IpSyncRunner(
    IpAddressCollector ipAddressCollector,
    IpStateStore ipStateStore,
    ReadmeRenderer readmeRenderer,
    GitPublisher gitPublisher,
    IOptions<IpSyncOptions> options,
    ILogger<IpSyncRunner> logger)
{
    private readonly IpSyncOptions options = options.Value;

    public async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        var repositoryPath = Path.GetFullPath(options.RepositoryPath);
        var readmePath = Path.Combine(repositoryPath, options.ReadmeFileName);

        logger.LogInformation("Capturing IP addresses for {RepositoryPath}", repositoryPath);

        Directory.CreateDirectory(repositoryPath);
        var snapshot = await ipAddressCollector.CaptureAsync(cancellationToken);

        var currentLocalSignature = ipStateStore.BuildLocalAddressSignature(snapshot.LocalAddresses);
        var previousState = await ipStateStore.LoadAsync(repositoryPath, readmePath, cancellationToken);

        if (previousState is not null &&
            string.Equals(previousState.LocalAddressSignature, currentLocalSignature, StringComparison.Ordinal))
        {
            logger.LogInformation("Local IP addresses did not change; skipping README update and Git publish.");
            await ipStateStore.SaveAsync(repositoryPath, snapshot, cancellationToken);
            return;
        }

        await gitPublisher.PullLatestAsync(repositoryPath, cancellationToken);

        var content = readmeRenderer.Render(snapshot);

        var tempPath = Path.Combine(repositoryPath, $".{Path.GetFileName(options.ReadmeFileName)}.tmp");
        await File.WriteAllTextAsync(tempPath, content, cancellationToken);
        File.Move(tempPath, readmePath, overwrite: true);

        logger.LogInformation("Updated {ReadmePath}", readmePath);

        await gitPublisher.PublishAsync(repositoryPath, readmePath, cancellationToken);
        await ipStateStore.SaveAsync(repositoryPath, snapshot, cancellationToken);
    }
}
