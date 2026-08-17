using System.Text.Json;
using System.Text.RegularExpressions;
using IpSync.Models;
using IpSync.Options;
using Microsoft.Extensions.Options;

namespace IpSync.Services;

public sealed class IpStateStore(
    IOptions<IpSyncOptions> options,
    ILogger<IpStateStore> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly IpSyncOptions options = options.Value;

    public string BuildLocalAddressSignature(IReadOnlyList<LocalIpAddress> localAddresses)
    {
        return BuildLocalAddressSignature(localAddresses.Select(address => (address.Version, address.Address)));
    }

    public async Task<IpSyncState?> LoadAsync(
        string repositoryPath,
        string readmePath,
        CancellationToken cancellationToken)
    {
        var statePath = GetStatePath(repositoryPath);

        if (File.Exists(statePath))
        {
            try
            {
                await using var stream = File.OpenRead(statePath);
                return await JsonSerializer.DeserializeAsync<IpSyncState>(
                    stream,
                    JsonOptions,
                    cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to read {StatePath}; falling back to README parsing.", statePath);
            }
        }

        var readmeAddresses = await ReadLocalAddressesFromReadmeAsync(readmePath, cancellationToken);
        if (readmeAddresses.Count == 0)
        {
            return null;
        }

        return new IpSyncState(
            BuildLocalAddressSignature(readmeAddresses),
            readmeAddresses.Select(address => FormatAddress(address.Version, address.Address)).ToArray(),
            DateTimeOffset.MinValue);
    }

    public async Task SaveAsync(
        string repositoryPath,
        IpSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        var localAddresses = snapshot.LocalAddresses
            .Select(address => FormatAddress(address.Version, address.Address))
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var state = new IpSyncState(
            BuildLocalAddressSignature(snapshot.LocalAddresses),
            localAddresses,
            snapshot.CapturedAtUtc);

        var statePath = GetStatePath(repositoryPath);
        var tempPath = $"{statePath}.tmp";
        var content = JsonSerializer.Serialize(state, JsonOptions);

        await File.WriteAllTextAsync(tempPath, content, cancellationToken);
        File.Move(tempPath, statePath, overwrite: true);
    }

    private string GetStatePath(string repositoryPath)
    {
        return Path.Combine(repositoryPath, options.StateFileName);
    }

    private static string BuildLocalAddressSignature(IEnumerable<(string Version, string Address)> localAddresses)
    {
        return string.Join(
            "\n",
            localAddresses
                .Select(address => FormatAddress(address.Version, address.Address))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Order(StringComparer.OrdinalIgnoreCase));
    }

    private static async Task<IReadOnlyList<(string Version, string Address)>> ReadLocalAddressesFromReadmeAsync(
        string readmePath,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(readmePath))
        {
            return [];
        }

        var addresses = new List<(string Version, string Address)>();
        var inLocalSection = false;
        var lines = await File.ReadAllLinesAsync(readmePath, cancellationToken);

        foreach (var line in lines)
        {
            if (line.StartsWith("## Local Network Addresses", StringComparison.OrdinalIgnoreCase))
            {
                inLocalSection = true;
                continue;
            }

            if (inLocalSection && line.StartsWith("## ", StringComparison.Ordinal))
            {
                break;
            }

            if (!inLocalSection || !line.StartsWith("| IPv", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var cells = line.Split('|', StringSplitOptions.TrimEntries);
            if (cells.Length < 4)
            {
                continue;
            }

            var version = cells[1];
            var address = ExtractInlineCode(cells[2]);

            if (!string.IsNullOrWhiteSpace(version) && !string.IsNullOrWhiteSpace(address))
            {
                addresses.Add((version, address));
            }
        }

        return addresses;
    }

    private static string? ExtractInlineCode(string value)
    {
        var match = Regex.Match(value, "`(?<value>[^`]+)`", RegexOptions.CultureInvariant);
        return match.Success ? match.Groups["value"].Value : null;
    }

    private static string FormatAddress(string version, string address)
    {
        return $"{version.Trim().ToUpperInvariant()}|{address.Trim().ToLowerInvariant()}";
    }
}
