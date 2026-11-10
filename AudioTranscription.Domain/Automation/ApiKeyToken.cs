using System.Security.Cryptography;
using System.Text;

namespace AudioTranscription.Domain.Automation;

/// <summary>
/// An API key (S28, ADR 0009): <c>at_&lt;prefix&gt;_&lt;secret&gt;</c>. The prefix (8 Base62 characters) is public and finds
/// the stored row; the secret (32 random bytes, Base64url) is never stored. Only the SHA-256 of the whole key is.
/// </summary>
public sealed record ApiKeyToken(string Key, string Prefix, byte[] Hash)
{
    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
    private const int PrefixLength = 8;
    private const string Marker = "at_";

    public static ApiKeyToken Generate()
    {
        var prefix = string.Create(PrefixLength, 0, static (span, _) =>
        {
            for (var i = 0; i < span.Length; i++)
                span[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        });
        var secret = Base64Url(RandomNumberGenerator.GetBytes(32));
        var key = $"{Marker}{prefix}_{secret}";
        return new ApiKeyToken(key, prefix, HashOf(key));
    }

    public static byte[] HashOf(string key) => SHA256.HashData(Encoding.UTF8.GetBytes(key));

    /// <summary>Constant-time comparison of the key's hash with the stored one.</summary>
    public static bool Verify(string key, byte[] storedHash) =>
        CryptographicOperations.FixedTimeEquals(HashOf(key), storedHash);

    /// <summary>Extracts the prefix of a well-formed key; false for anything else (no lookup is made then).</summary>
    public static bool TryParsePrefix(string? key, out string prefix)
    {
        prefix = string.Empty;
        if (string.IsNullOrEmpty(key) || key.Length > 200 || !key.StartsWith(Marker, StringComparison.Ordinal))
            return false;

        var separator = Marker.Length + PrefixLength;
        if (key.Length <= separator + 1 || key[separator] != '_')
            return false;

        var candidate = key.Substring(Marker.Length, PrefixLength);
        if (candidate.Any(c => !Alphabet.Contains(c)))
            return false;

        var secret = key[(separator + 1)..];
        if (secret.Length < 43 || secret.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '-' or '_')))
            return false;

        prefix = candidate;
        return true;
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

/// <summary>Scopes of an API key and which paths a key may reach at all (ADR 0009).</summary>
public static class ApiKeyScopes
{
    public const string JobsRead = "jobs:read";
    public const string JobsWrite = "jobs:write";

    public static readonly IReadOnlyList<string> All = [JobsRead, JobsWrite];

    /// <summary>Claim type the authentication handler puts the key's scopes under.</summary>
    public const string ClaimType = "scope";

    /// <summary>At least one scope, only known ones, no duplicates.</summary>
    public static bool AreValid(IReadOnlyCollection<string> scopes) =>
        scopes.Count > 0 && scopes.All(All.Contains) && scopes.Distinct().Count() == scopes.Count;

    public static string RequiredFor(string httpMethod) =>
        HttpMethods.IsReadOnly(httpMethod) ? JobsRead : JobsWrite;

    /// <summary>Resumable uploads (tus) also use HEAD to ask for the offset; everything below /api/uploads is writing.</summary>
    public static string RequiredFor(string path, string httpMethod) =>
        path.StartsWith("/api/uploads", StringComparison.OrdinalIgnoreCase) ? JobsWrite : RequiredFor(httpMethod);

    /// <summary>Keys reach the job API only: never key, webhook, admin or account management.</summary>
    public static bool IsPathAllowed(string path) =>
        AllowedPrefixes.Any(prefix =>
            path.Equals(prefix, StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase));

    private static readonly string[] AllowedPrefixes = ["/api/audio-jobs", "/api/uploads", "/api/search"];

    private static class HttpMethods
    {
        public static bool IsReadOnly(string method) =>
            method.Equals("GET", StringComparison.OrdinalIgnoreCase) ||
            method.Equals("HEAD", StringComparison.OrdinalIgnoreCase) ||
            method.Equals("OPTIONS", StringComparison.OrdinalIgnoreCase);
    }
}
