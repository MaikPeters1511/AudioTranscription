using System.Net;
using System.Net.Sockets;
using System.Text;
using AudioTranscription.Domain.Automation;
using AudioTranscription.Domain.Entities;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;

namespace AudioTranscription.Api.Automation;

/// <summary>Configuration of webhooks (S28, ADR 0009), section <c>Webhooks</c>.</summary>
public class WebhookOptions
{
    public const string SectionName = "Webhooks";

    /// <summary>Operator switch: allow plain http targets. Default off.</summary>
    public bool AllowHttp { get; set; }

    /// <summary>
    /// Operator switch: allow targets in private networks (e.g. an n8n in the same Docker network). Link-local addresses
    /// (cloud metadata) stay blocked either way. Default off.
    /// </summary>
    public bool AllowPrivateTargets { get; set; }

    /// <summary>Waits between attempts; one more attempt than entries: the first one is immediate.</summary>
    public TimeSpan[] RetryDelays { get; set; } =
        [TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(10), TimeSpan.FromHours(1), TimeSpan.FromHours(6)];

    public int RequestTimeoutSeconds { get; set; } = 10;
    public int PollIntervalSeconds { get; set; } = 5;
    public int RetentionDays { get; set; } = 30;
}

/// <summary>The target is not allowed (internal address); never retried.</summary>
public class WebhookTargetBlockedException(string message) : Exception(message);

/// <summary>Resolves a webhook host and checks the addresses right before connecting, against DNS rebinding.</summary>
public static class SsrfSafeConnect
{
    public static async Task<IPAddress[]> ResolveAsync(
        string host, bool allowPrivate, Func<string, CancellationToken, Task<IPAddress[]>> resolve, CancellationToken cancellationToken)
    {
        var addresses = IPAddress.TryParse(host.Trim('[', ']'), out var literal)
            ? [literal]
            : await resolve(host, cancellationToken);

        // One internal address is enough to refuse the name: the connection could pick it
        if (addresses.Length == 0 || addresses.Any(a => WebhookTargetPolicy.IsBlockedAddress(a, allowPrivate)))
            throw new WebhookTargetBlockedException("The target address is not allowed.");
        return addresses;
    }

    /// <summary>Connect callback of the webhook HTTP client: connects only to addresses that passed <see cref="ResolveAsync"/>.</summary>
    public static async ValueTask<Stream> ConnectAsync(SocketsHttpConnectionContext context, bool allowPrivate, CancellationToken cancellationToken)
    {
        var addresses = await ResolveAsync(context.DnsEndPoint.Host, allowPrivate, Dns.GetHostAddressesAsync, cancellationToken);
        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(addresses, context.DnsEndPoint.Port, cancellationToken);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }
}

/// <summary>Encrypts webhook secrets for storage (ADR 0009): HMAC needs the secret itself, a hash would not do.</summary>
public class WebhookSecretProtector(IDataProtectionProvider provider)
{
    private readonly IDataProtector _protector = provider.CreateProtector("AudioTranscription.Webhooks.Secret.v1");

    public string Protect(string secret) => _protector.Protect(secret);
    public string Unprotect(string protectedSecret) => _protector.Unprotect(protectedSecret);
}

/// <summary>
/// The HTTP client for webhook calls. Deliberately not a factory client: the shared defaults (standard resilience handler
/// with its own retries, service discovery that could rewrite names to internal service addresses) must not apply, because
/// retries are the delivery log's job (ADR 0009) and the target must be exactly what was validated.
/// </summary>
public sealed class WebhookHttpClient(HttpClient client)
{
    public HttpClient Client { get; } = client;

    public static WebhookHttpClient Create(WebhookOptions options)
    {
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,        // redirect limit 0: a redirect would bypass the target check
            UseProxy = false,
            UseCookies = false,
            ConnectTimeout = TimeSpan.FromSeconds(5),
            PooledConnectionLifetime = TimeSpan.FromMinutes(2),   // re-resolve DNS regularly
            ConnectCallback = (context, cancellationToken) => SsrfSafeConnect.ConnectAsync(context, options.AllowPrivateTargets, cancellationToken),
        };
        return new WebhookHttpClient(new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(Math.Max(1, options.RequestTimeoutSeconds)) });
    }
}

public record WebhookSendResult(int? StatusCode, string? Error, bool Permanent)
{
    public bool Success => StatusCode is { } code && WebhookRetryPolicy.IsSuccess(code);
}

/// <summary>Sends one signed request. Does not follow redirects, never reads the response body, never reveals error details.</summary>
public class WebhookSender(WebhookHttpClient http, IOptions<WebhookOptions> options, TimeProvider time)
{
    public async Task<WebhookSendResult> SendAsync(Webhook webhook, string secret, WebhookDelivery delivery, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        // Checked again here: the configuration may have changed since the URL was saved
        if (WebhookTargetPolicy.Validate(webhook.Url, settings.AllowHttp, settings.AllowPrivateTargets) is not null)
            return new WebhookSendResult(null, "blocked target", Permanent: true);

        var timestamp = time.GetUtcNow().ToUnixTimeSeconds();
        using var request = new HttpRequestMessage(HttpMethod.Post, webhook.Url)
        {
            Content = new StringContent(delivery.Payload, Encoding.UTF8, "application/json"),
        };
        request.Headers.TryAddWithoutValidation("User-Agent", "AudioTranscription-Webhook/1.0");
        request.Headers.TryAddWithoutValidation("X-Webhook-Id", delivery.Id.ToString());
        request.Headers.TryAddWithoutValidation("X-Webhook-Event", delivery.Event);
        request.Headers.TryAddWithoutValidation("X-Webhook-Timestamp", timestamp.ToString(System.Globalization.CultureInfo.InvariantCulture));
        request.Headers.TryAddWithoutValidation("X-Webhook-Signature", WebhookSigner.Sign(secret, timestamp, delivery.Payload));

        try
        {
            using var response = await http.Client
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            var status = (int)response.StatusCode;
            return WebhookRetryPolicy.IsSuccess(status)
                ? new WebhookSendResult(status, null, false)
                : new WebhookSendResult(status, $"http {status}", !WebhookRetryPolicy.IsRetryable(status));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new WebhookSendResult(null, "timeout", false);
        }
        catch (Exception ex) when (ex is WebhookTargetBlockedException || ex.InnerException is WebhookTargetBlockedException)
        {
            return new WebhookSendResult(null, "blocked target", Permanent: true);
        }
        catch (HttpRequestException)
        {
            return new WebhookSendResult(null, "connection error", false);
        }
    }
}
