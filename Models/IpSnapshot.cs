namespace IpSync.Models;

public sealed record IpSnapshot(
    DateTimeOffset CapturedAtUtc,
    DateTimeOffset CapturedAtLocal,
    string MachineName,
    IReadOnlyList<LocalIpAddress> LocalAddresses,
    IReadOnlyList<ExternalIpAddress> ExternalAddresses);

public sealed record LocalIpAddress(
    string Version,
    string Address,
    int? PrefixLength,
    string InterfaceName,
    string InterfaceDescription,
    string NetworkKind);

public sealed record ExternalIpAddress(
    string Version,
    string? Address,
    string? Source,
    string? Error);

public sealed record IpSyncState(
    string LocalAddressSignature,
    string[] LocalAddresses,
    DateTimeOffset LastSeenAtUtc);
