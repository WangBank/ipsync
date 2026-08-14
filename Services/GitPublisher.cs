using System.Diagnostics;
using IpSync.Options;
using Microsoft.Extensions.Options;

namespace IpSync.Services;

public sealed class GitPublisher(
    IOptions<IpSyncOptions> options,
    ILogger<GitPublisher> logger)
{
    private readonly IpSyncOptions options = options.Value;

    public async Task PullLatestAsync(string repositoryPath, CancellationToken cancellationToken)
    {
        if (!CanUseGit(repositoryPath))
        {
            return;
        }

        if (!options.Git.PullBeforePush)
        {
            return;
        }

        await RunGitAsync(repositoryPath, BuildPullArguments(), cancellationToken);
    }

    public async Task PublishAsync(string repositoryPath, string readmePath, CancellationToken cancellationToken)
    {
        if (!CanUseGit(repositoryPath))
        {
            return;
        }

        var relativeReadmePath = Path.GetRelativePath(repositoryPath, readmePath);
        await RunGitAsync(repositoryPath, $"add -- {Quote(relativeReadmePath)}", cancellationToken);

        var status = await RunGitAsync(
            repositoryPath,
            $"status --porcelain -- {Quote(relativeReadmePath)}",
            cancellationToken,
            throwOnFailure: true);

        if (string.IsNullOrWhiteSpace(status.StandardOutput))
        {
            logger.LogInformation("README did not change; nothing to commit.");
            return;
        }

        var commit = await RunGitAsync(
            repositoryPath,
            $"commit -m {Quote(options.Git.CommitMessage)} -- {Quote(relativeReadmePath)}",
            cancellationToken,
            throwOnFailure: false);

        if (commit.ExitCode != 0)
        {
            if (commit.StandardOutput.Contains("nothing to commit", StringComparison.OrdinalIgnoreCase) ||
                commit.StandardError.Contains("nothing to commit", StringComparison.OrdinalIgnoreCase))
            {
                logger.LogInformation("README did not change; nothing to commit.");
                return;
            }

            throw new InvalidOperationException(
                $"git commit failed with exit code {commit.ExitCode}: {commit.StandardError}{commit.StandardOutput}");
        }

        await RunGitAsync(repositoryPath, BuildPushArguments(), cancellationToken);
    }

    private bool CanUseGit(string repositoryPath)
    {
        if (!options.Git.Enabled)
        {
            logger.LogInformation("Git publishing is disabled.");
            return false;
        }

        if (!Directory.Exists(Path.Combine(repositoryPath, ".git")))
        {
            logger.LogWarning("Skipping Git publish because {RepositoryPath} is not a Git repository.", repositoryPath);
            return false;
        }

        return true;
    }

    private async Task<GitResult> RunGitAsync(
        string workingDirectory,
        string arguments,
        CancellationToken cancellationToken,
        bool throwOnFailure = true)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = "git",
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        logger.LogInformation("Running git {Arguments}", arguments);

        process.Start();
        var standardOutputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var standardErrorTask = process.StandardError.ReadToEndAsync(cancellationToken);

        await process.WaitForExitAsync(cancellationToken);

        var result = new GitResult(
            process.ExitCode,
            await standardOutputTask,
            await standardErrorTask);

        if (throwOnFailure && result.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"git {arguments} failed with exit code {result.ExitCode}: {result.StandardError}{result.StandardOutput}");
        }

        return result;
    }

    private string BuildPullArguments()
    {
        return string.IsNullOrWhiteSpace(options.Git.Branch)
            ? "pull --ff-only"
            : $"pull --ff-only {Quote(options.Git.RemoteName)} {Quote(options.Git.Branch)}";
    }

    private string BuildPushArguments()
    {
        return string.IsNullOrWhiteSpace(options.Git.Branch)
            ? "push"
            : $"push {Quote(options.Git.RemoteName)} {Quote(options.Git.Branch)}";
    }

    private static string Quote(string value)
    {
        return $"\"{value.Replace("\"", "\\\"", StringComparison.Ordinal)}\"";
    }

    private sealed record GitResult(int ExitCode, string StandardOutput, string StandardError);
}
