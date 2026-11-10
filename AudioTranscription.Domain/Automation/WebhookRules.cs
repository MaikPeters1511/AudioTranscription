using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace AudioTranscription.Domain.Automation;

/// <summary>Events a webhook can subscribe to (ADR 0009).</summary>
public static class WebhookEvents
{
    public const string JobCompleted = "job.completed";
    public const string JobFailed = "job.failed";
    /// <summary>Only sent by <c>POST /api/webhooks/{id}/test</c>; not subscribable.</summary>
    public const string Test = "webhook.test";

    public static readonly IReadOnlyList<string> Subscribable = [JobCompleted, JobFailed];

    public static bool AreValid(IReadOnlyCollection<string> events) =>
        events.Count > 0 && events.All(Subscribable.Contains) && events.Distinct().Count() == events.Count;
}

/// <summary>
/// HMAC-SHA256 signature of a delivery: <c>v1=hex(HMAC(secret, "{timestamp}.{body}"))</c>. The timestamp is part of
/// the signed text so a receiver can reject replays of old recordings.
/// </summary>
public static class WebhookSigner
{
    public const string Version = "v1";

    public static string GenerateSecret() =>
        "whsec_" + Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static string Sign(string secret, long unixSeconds, string body)
    {
        var text = unixSeconds.ToString(CultureInfo.InvariantCulture) + "." + body;
        var mac = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(text));
        return Version + "=" + Convert.ToHexStringLower(mac);
    }

    public static bool Verify(string secret, long unixSeconds, string body, string signature) =>
        CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(Sign(secret, unixSeconds, body)), Encoding.UTF8.GetBytes(signature));
}

/// <summary>When a failed delivery is tried again (ADR 0009): growing delays, a few refusals are final.</summary>
public static class WebhookRetryPolicy
{
    public static bool IsSuccess(int statusCode) => statusCode is >= 200 and < 300;

    /// <summary>
    /// A 4xx is the receiver's explicit "no" and is not repeated, except "request timeout" (408) and "too many requests"
    /// (429). Everything else (5xx, redirects, which are never followed) is tried again.
    /// </summary>
    public static bool IsRetryable(int statusCode) =>
        !IsSuccess(statusCode) && (statusCode is 408 or 429 || statusCode is < 400 or >= 500);

    /// <param name="attemptsMade">How many attempts have failed so far (at least 1).</param>
    /// <returns>The wait before the next attempt, or null when no attempts are left.</returns>
    public static TimeSpan? NextDelay(int attemptsMade, IReadOnlyList<TimeSpan> delays) =>
        attemptsMade >= 1 && attemptsMade <= delays.Count ? delays[attemptsMade - 1] : null;
}

/// <summary>
/// SSRF protection for webhook targets (ADR 0009). The URL is checked when saved and the resolved addresses again
/// when connecting, so a name that later points to an internal address is refused as well.
/// </summary>
public static class WebhookTargetPolicy
{
    public const int MaxUrlLength = 2048;

    private static readonly string[] InternalSuffixes = [".localhost", ".local", ".internal", ".localdomain"];

    /// <returns>An English reason when the target is not allowed, otherwise null.</returns>
    public static string? Validate(string? url, bool allowHttp, bool allowPrivate)
    {
        if (string.IsNullOrWhiteSpace(url))
            return "The URL is required.";
        if (url.Length > MaxUrlLength)
            return $"The URL must not be longer than {MaxUrlLength} characters.";
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri))
            return "The URL is not valid.";

        var schemeOk = uri.Scheme == Uri.UriSchemeHttps || (allowHttp && uri.Scheme == Uri.UriSchemeHttp);
        if (!schemeOk)
            return allowHttp ? "Only http and https URLs are allowed." : "Only https URLs are allowed.";
        if (!string.IsNullOrEmpty(uri.UserInfo))
            return "The URL must not contain credentials.";
        if (!string.IsNullOrEmpty(uri.Fragment))
            return "The URL must not contain a fragment.";

        var host = uri.IdnHost.TrimEnd('.').ToLowerInvariant();
        if (host.Length == 0)
            return "The URL has no host.";

        if (IPAddress.TryParse(host.Trim('[', ']'), out var address))
            return IsBlockedAddress(address, allowPrivate) ? "The target address is not allowed." : null;

        if (!allowPrivate && IsInternalName(host))
            return "Internal host names are not allowed.";
        if (host == "localhost" || host.EndsWith(".localhost", StringComparison.Ordinal))
            return "Internal host names are not allowed.";
        return null;
    }

    private static bool IsInternalName(string host) =>
        host == "localhost" || InternalSuffixes.Any(suffix => host.EndsWith(suffix, StringComparison.Ordinal));

    /// <summary>
    /// True for loopback, private, link-local, shared (CGNAT), unspecified, multicast and broadcast addresses, also when
    /// hidden in an IPv4-mapped IPv6 address. With <paramref name="allowPrivate"/> only link-local (cloud metadata),
    /// unspecified, multicast and broadcast stay blocked.
    /// </summary>
    public static bool IsBlockedAddress(IPAddress address, bool allowPrivate)
    {
        if (address.IsIPv4MappedToIPv6)
            address = address.MapToIPv4();

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = address.GetAddressBytes();
            if (b[0] == 0) return true;                                   // 0.0.0.0/8
            if (b[0] >= 224) return true;                                 // multicast, reserved, broadcast
            if (b[0] == 169 && b[1] == 254) return true;                  // link-local, cloud metadata
            if (allowPrivate) return false;
            if (b[0] == 127) return true;                                 // loopback
            if (b[0] == 10) return true;                                  // 10/8
            if (b[0] == 172 && b[1] is >= 16 and <= 31) return true;      // 172.16/12
            if (b[0] == 192 && b[1] == 168) return true;                  // 192.168/16
            if (b[0] == 100 && b[1] is >= 64 and <= 127) return true;     // 100.64/10 carrier-grade NAT
            return false;
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (address.IsIPv6Multicast || address.IsIPv6LinkLocal || address.IsIPv6SiteLocal) return true;
            if (address.Equals(IPAddress.IPv6Any)) return true;
            var b = address.GetAddressBytes();
            // An IPv4 address hidden in an IPv6 one (NAT64 64:ff9b::/96, 6to4 2002::/16, IPv4-compatible ::a.b.c.d)
            // is judged like the IPv4 address itself
            if (EmbeddedIPv4(b) is { } embedded)
                return IsBlockedAddress(embedded, allowPrivate);
            if (allowPrivate) return false;
            if (IPAddress.IsLoopback(address)) return true;
            return (b[0] & 0xFE) == 0xFC;                                 // fc00::/7 unique local
        }

        return true; // unknown address family
    }

    private static IPAddress? EmbeddedIPv4(byte[] b)
    {
        // 64:ff9b::/96
        if (b[0] == 0x00 && b[1] == 0x64 && b[2] == 0xFF && b[3] == 0x9B && b[4..12].All(x => x == 0))
            return new IPAddress(b[12..16]);
        // 2002::/16: the IPv4 address follows the prefix
        if (b[0] == 0x20 && b[1] == 0x02)
            return new IPAddress(b[2..6]);
        // ::a.b.c.d (but not :: and ::1, which are handled as such)
        if (b[..12].All(x => x == 0) && !(b[12..15].All(x => x == 0) && b[15] <= 1))
            return new IPAddress(b[12..16]);
        return null;
    }
}
