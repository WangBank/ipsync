namespace IpSync.Options;

public sealed class IpSyncOptions
{
    public const string SectionName = "IpSync";

    public string RepositoryPath { get; set; } = AppContext.BaseDirectory;

    public string ReadmeFileName { get; set; } = "README.md";

    public int IntervalMinutes { get; set; } = 10;

    public GitOptions Git { get; set; } = new();

    public ExternalIpEndpointsOptions ExternalIpEndpoints { get; set; } = new();
}

public sealed class GitOptions
{
    public bool Enabled { get; set; } = true;

    public bool PullBeforePush { get; set; } = true;

    public string RemoteName { get; set; } = "origin";

    public string Branch { get; set; } = string.Empty;

    public string CommitMessage { get; set; } = "Update IP address snapshot";
}

public sealed class ExternalIpEndpointsOptions
{
    public string[] IPv4 { get; set; } = [];

    public string[] IPv6 { get; set; } = [];
}
