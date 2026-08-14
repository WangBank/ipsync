using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using IpSync.Models;
using IpSync.Options;
using Microsoft.Extensions.Options;

namespace IpSync.Services;

public sealed class IpAddressCollector(
    HttpClient httpClient,
    IOptions<IpSyncOptions> options,
    ILogger<IpAddressCollector> logger)
{
    private readonly IpSyncOptions options = options.Value;

    public async Task<IpSnapshot> CaptureAsync(CancellationToken cancellationToken)
    {
        var localAddresses = GetLocalAddresses();
        var externalAddresses = new List<ExternalIpAddress>
        {
            await GetExternalAddressAsync(
                "IPv4",
                AddressFamily.InterNetwork,
                options.ExternalIpEndpoints.IPv4,
                cancellationToken),
            await GetExternalAddressAsync(
                "IPv6",
                AddressFamily.InterNetworkV6,
                options.ExternalIpEndpoints.IPv6,
                cancellationToken)
        };

        return new IpSnapshot(
            DateTimeOffset.UtcNow,
            DateTimeOffset.Now,
            Environment.MachineName,
            localAddresses,
            externalAddresses);
    }

    private static IReadOnlyList<LocalIpAddress> GetLocalAddresses()
    {
        return NetworkInterface.GetAllNetworkInterfaces()
            .Where(networkInterface => networkInterface.OperationalStatus == OperationalStatus.Up)
            .Where(networkInterface => networkInterface.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .SelectMany(networkInterface =>
                networkInterface.GetIPProperties().UnicastAddresses
                    .Where(address => IsSupportedAddress(address.Address))
                    .Select(address => new LocalIpAddress(
                        GetVersion(address.Address),
                        address.Address.ToString(),
                        TryGetPrefixLength(address),
                        networkInterface.Name,
                        networkInterface.Description,
                        GetNetworkKind(address.Address))))
            .OrderBy(address => address.Version)
            .ThenBy(address => address.InterfaceName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(address => address.Address, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private async Task<ExternalIpAddress> GetExternalAddressAsync(
        string version,
        AddressFamily addressFamily,
        IReadOnlyCollection<string> endpoints,
        CancellationToken cancellationToken)
    {
        if (endpoints.Count == 0)
        {
            return new ExternalIpAddress(version, null, null, "No endpoint configured.");
        }

        var failures = new List<string>();

        foreach (var endpoint in endpoints.Where(endpoint => !string.IsNullOrWhiteSpace(endpoint)))
        {
            try
            {
                using var response = await httpClient.GetAsync(endpoint, cancellationToken);
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                response.EnsureSuccessStatusCode();

                var candidate = body
                    .Split(['\r', '\n', ' ', '\t'], StringSplitOptions.RemoveEmptyEntries)
                    .FirstOrDefault();

                if (candidate is not null &&
                    IPAddress.TryParse(candidate, out var address) &&
                    address.AddressFamily == addressFamily)
                {
                    return new ExternalIpAddress(version, address.ToString(), endpoint, null);
                }

                failures.Add($"{endpoint}: unexpected response");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Failed to read external {Version} address from {Endpoint}", version, endpoint);
                failures.Add($"{endpoint}: {ex.Message}");
            }
        }

        return new ExternalIpAddress(version, null, null, string.Join("; ", failures));
    }

    private static bool IsSupportedAddress(IPAddress address)
    {
        return (address.AddressFamily is AddressFamily.InterNetwork or AddressFamily.InterNetworkV6) &&
               !IPAddress.IsLoopback(address);
    }

    private static string GetVersion(IPAddress address)
    {
        return address.AddressFamily == AddressFamily.InterNetwork ? "IPv4" : "IPv6";
    }

    private static int? TryGetPrefixLength(UnicastIPAddressInformation address)
    {
        try
        {
            return address.PrefixLength;
        }
        catch (PlatformNotSupportedException)
        {
            return null;
        }
    }

    private static string GetNetworkKind(IPAddress address)
    {
        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var bytes = address.GetAddressBytes();
            return bytes switch
            {
                [10, ..] => "Private",
                [172, >= 16 and <= 31, ..] => "Private",
                [192, 168, ..] => "Private",
                [169, 254, ..] => "Link-local",
                [100, >= 64 and <= 127, ..] => "Carrier-grade NAT",
                [198, 18 or 19, ..] => "Benchmark",
                _ => "Public"
            };
        }

        var ipv6Bytes = address.GetAddressBytes();

        if (address.IsIPv6LinkLocal)
        {
            return "Link-local";
        }

        if ((ipv6Bytes[0] & 0xfe) == 0xfc)
        {
            return "Unique-local";
        }

        return "Global";
    }
}
