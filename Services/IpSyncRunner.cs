using IpSync.Options;
using Microsoft.Extensions.Options;

namespace IpSync.Services;

public sealed class IpSyncRunner(
    IpAddressCollector ipAddressCollector,
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
        await gitPublisher.PullLatestAsync(repositoryPath, cancellationToken);

        var snapshot = await ipAddressCollector.CaptureAsync(cancellationToken);
        var content = readmeRenderer.Render(snapshot);

        var tempPath = Path.Combine(repositoryPath, $".{Path.GetFileName(options.ReadmeFileName)}.tmp");
        await File.WriteAllTextAsync(tempPath, content, cancellationToken);
        File.Move(tempPath, readmePath, overwrite: true);

        logger.LogInformation("Updated {ReadmePath}", readmePath);

        await gitPublisher.PublishAsync(repositoryPath, readmePath, cancellationToken);
    }
}
