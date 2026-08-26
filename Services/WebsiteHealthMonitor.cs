using System.Net;
using System.Runtime.Versioning;
using System.ServiceProcess;
using IpSync.Options;
using Microsoft.Extensions.Options;

namespace IpSync.Services;

public sealed class WebsiteHealthMonitor(
    HttpClient httpClient,
    IOptions<IpSyncOptions> options,
    ILogger<WebsiteHealthMonitor> logger)
{
    private const int ResponseSampleLength = 4096;
    private readonly IpSyncOptions options = options.Value;

    public async Task CheckAndRecoverAsync(CancellationToken cancellationToken)
    {
        var healthOptions = options.WebsiteHealth;
        if (!healthOptions.Enabled || string.IsNullOrWhiteSpace(healthOptions.Url))
        {
            return;
        }

        if (!Uri.TryCreate(healthOptions.Url, UriKind.Absolute, out var websiteUri))
        {
            logger.LogError("Website health URL is invalid: {WebsiteUrl}", healthOptions.Url);
            return;
        }

        try
        {
            using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutSource.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(healthOptions.RequestTimeoutSeconds, 1, 120)));

            using var request = new HttpRequestMessage(HttpMethod.Get, websiteUri);
            request.Headers.UserAgent.ParseAdd("IpSync/1.0");

            using var response = await httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                timeoutSource.Token);

            var responseSample = response.IsSuccessStatusCode
                ? string.Empty
                : await ReadResponseSampleAsync(response.Content, timeoutSource.Token);

            if (!IsCloudflareError(response.StatusCode, response.Headers.Server.ToString(), responseSample))
            {
                logger.LogInformation(
                    "Website health check returned HTTP {StatusCode} for {WebsiteUrl}; no Cloudflared restart is needed.",
                    (int)response.StatusCode,
                    websiteUri);
                return;
            }

            logger.LogWarning(
                "Website {WebsiteUrl} returned Cloudflare error HTTP {StatusCode}; restarting Windows service {ServiceName}.",
                websiteUri,
                (int)response.StatusCode,
                healthOptions.CloudflaredServiceName);

            if (!OperatingSystem.IsWindows())
            {
                logger.LogError("Cloudflared recovery requires Windows; the service was not restarted.");
                return;
            }

            await RestartServiceAsync(
                healthOptions.CloudflaredServiceName,
                TimeSpan.FromSeconds(Math.Clamp(healthOptions.ServiceOperationTimeoutSeconds, 5, 300)),
                cancellationToken);

            logger.LogInformation("Windows service {ServiceName} restarted successfully.", healthOptions.CloudflaredServiceName);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            logger.LogWarning("Website health check timed out for {WebsiteUrl}; Cloudflared was not restarted.", websiteUri);
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "Website health check could not reach {WebsiteUrl}; Cloudflared was not restarted.", websiteUri);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Cloudflare health recovery failed for {WebsiteUrl}. IP synchronization will continue.",
                websiteUri);
        }
    }

    internal static bool IsCloudflareError(HttpStatusCode statusCode, string serverHeader, string responseSample)
    {
        var numericStatusCode = (int)statusCode;
        if (numericStatusCode is >= 520 and <= 530)
        {
            return true;
        }

        if (numericStatusCode is < 500 or > 599)
        {
            return false;
        }

        return serverHeader.Contains("cloudflare", StringComparison.OrdinalIgnoreCase) ||
               responseSample.Contains("cf-error-details", StringComparison.OrdinalIgnoreCase) ||
               responseSample.Contains("cloudflare ray id", StringComparison.OrdinalIgnoreCase) ||
               responseSample.Contains("error code: 10", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<string> ReadResponseSampleAsync(
        HttpContent content,
        CancellationToken cancellationToken)
    {
        await using var stream = await content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);
        var buffer = new char[ResponseSampleLength];
        var charactersRead = await reader.ReadBlockAsync(buffer.AsMemory(), cancellationToken);
        return new string(buffer, 0, charactersRead);
    }

    [SupportedOSPlatform("windows")]
    private static async Task RestartServiceAsync(
        string serviceName,
        TimeSpan operationTimeout,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(serviceName))
        {
            throw new InvalidOperationException("Cloudflared service name is not configured.");
        }

        using var service = new ServiceController(serviceName);
        service.Refresh();

        if (service.Status == ServiceControllerStatus.StartPending)
        {
            await WaitForStatusAsync(service, ServiceControllerStatus.Running, operationTimeout, cancellationToken);
        }
        else if (service.Status == ServiceControllerStatus.StopPending)
        {
            await WaitForStatusAsync(service, ServiceControllerStatus.Stopped, operationTimeout, cancellationToken);
        }

        service.Refresh();
        if (service.Status != ServiceControllerStatus.Stopped)
        {
            if (!service.CanStop)
            {
                throw new InvalidOperationException($"Windows service '{serviceName}' cannot be stopped.");
            }

            service.Stop();
            await WaitForStatusAsync(service, ServiceControllerStatus.Stopped, operationTimeout, cancellationToken);
        }

        service.Start();
        await WaitForStatusAsync(service, ServiceControllerStatus.Running, operationTimeout, cancellationToken);
    }

    [SupportedOSPlatform("windows")]
    private static async Task WaitForStatusAsync(
        ServiceController service,
        ServiceControllerStatus expectedStatus,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow.Add(timeout);

        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            service.Refresh();

            if (service.Status == expectedStatus)
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);
        }

        service.Refresh();
        throw new System.TimeoutException(
            $"Windows service '{service.ServiceName}' did not reach state '{expectedStatus}' within {timeout.TotalSeconds:0} seconds. Current state: '{service.Status}'.");
    }
}
