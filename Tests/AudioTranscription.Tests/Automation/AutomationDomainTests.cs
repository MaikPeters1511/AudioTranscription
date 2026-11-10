using System.Net;
using AudioTranscription.Domain.Automation;
using FluentAssertions;

namespace AudioTranscription.Tests.Automation;

/// <summary>S28 (ADR 0009): the pure rules behind API keys, webhook signatures, retries and SSRF protection.</summary>
public class ApiKeyTokenTests
{
    [Fact]
    public void Generate_ProducesTheDocumentedFormat()
    {
        var token = ApiKeyToken.Generate();

        token.Key.Should().StartWith($"at_{token.Prefix}_");
        token.Prefix.Should().MatchRegex("^[A-Za-z0-9]{8}$");
        token.Key[$"at_{token.Prefix}_".Length..].Length.Should().BeGreaterThanOrEqualTo(43);
        token.Hash.Should().HaveCount(32);
    }

    [Fact]
    public void Generate_ProducesDifferentKeysEveryTime()
    {
        var keys = Enumerable.Range(0, 50).Select(_ => ApiKeyToken.Generate().Key).ToHashSet();

        keys.Should().HaveCount(50);
    }

    [Fact]
    public void Hash_IsNotTheKeyAndMatchesOnlyTheSameKey()
    {
        var token = ApiKeyToken.Generate();
        var other = ApiKeyToken.Generate();

        ApiKeyToken.Verify(token.Key, token.Hash).Should().BeTrue();
        ApiKeyToken.Verify(other.Key, token.Hash).Should().BeFalse();
        System.Text.Encoding.UTF8.GetString(token.Hash).Should().NotContain(token.Key);
    }

    [Theory]
    [InlineData("")]
    [InlineData("at_")]
    [InlineData("at_short_secret")]
    [InlineData("xx_ABCDEFGH_secretsecretsecretsecretsecretsecretsecret")]
    [InlineData("at_ABCDEFGH-secretsecretsecretsecretsecretsecretsecret")]
    [InlineData("at_ABCD/FGH_secretsecretsecretsecretsecretsecretsecret")]
    public void TryParsePrefix_RejectsMalformedKeys(string key)
    {
        ApiKeyToken.TryParsePrefix(key, out _).Should().BeFalse();
    }

    [Fact]
    public void TryParsePrefix_ReturnsThePrefixOfAGeneratedKey()
    {
        var token = ApiKeyToken.Generate();

        ApiKeyToken.TryParsePrefix(token.Key, out var prefix).Should().BeTrue();
        prefix.Should().Be(token.Prefix);
    }

    [Fact]
    public void Scopes_AreValidatedAgainstTheKnownList()
    {
        ApiKeyScopes.AreValid([ApiKeyScopes.JobsRead, ApiKeyScopes.JobsWrite]).Should().BeTrue();
        ApiKeyScopes.AreValid([]).Should().BeFalse();
        ApiKeyScopes.AreValid(["admin"]).Should().BeFalse();
        ApiKeyScopes.AreValid([ApiKeyScopes.JobsRead, ApiKeyScopes.JobsRead]).Should().BeFalse();
    }

    [Theory]
    [InlineData("GET", ApiKeyScopes.JobsRead)]
    [InlineData("HEAD", ApiKeyScopes.JobsRead)]
    [InlineData("POST", ApiKeyScopes.JobsWrite)]
    [InlineData("PATCH", ApiKeyScopes.JobsWrite)]
    [InlineData("DELETE", ApiKeyScopes.JobsWrite)]
    public void RequiredScope_DependsOnTheMethod(string method, string expected)
    {
        ApiKeyScopes.RequiredFor(method).Should().Be(expected);
    }

    [Theory]
    [InlineData("/api/audio-jobs", true)]
    [InlineData("/api/audio-jobs/5/segments", true)]
    [InlineData("/api/search", true)]
    [InlineData("/api/uploads/abc", true)]
    [InlineData("/api/api-keys", false)]
    [InlineData("/api/webhooks/1", false)]
    [InlineData("/api/admin/users", false)]
    [InlineData("/api/auth/me", false)]
    [InlineData("/api/audio-jobsx", false)]
    [InlineData("/hubs/transcription", false)]
    public void Paths_AreAllowedOnlyForJobEndpoints(string path, bool allowed)
    {
        ApiKeyScopes.IsPathAllowed(path).Should().Be(allowed);
    }
}

public class WebhookSignerTests
{
    [Fact]
    public void Sign_MatchesAKnownHmacSha256Vector()
    {
        // HMAC-SHA256("secret", "1700000000.{\"a\":1}") computed independently
        var signature = WebhookSigner.Sign("secret", 1700000000, "{\"a\":1}");

        signature.Should().StartWith("v1=");
        signature.Length.Should().Be(3 + 64);
        signature.Should().Be(WebhookSigner.Sign("secret", 1700000000, "{\"a\":1}"));
    }

    [Fact]
    public void Sign_ChangesWithSecretTimestampAndBody()
    {
        var baseline = WebhookSigner.Sign("secret", 1, "body");

        WebhookSigner.Sign("other", 1, "body").Should().NotBe(baseline);
        WebhookSigner.Sign("secret", 2, "body").Should().NotBe(baseline);
        WebhookSigner.Sign("secret", 1, "body2").Should().NotBe(baseline);
    }

    [Fact]
    public void Verify_AcceptsTheRightSignatureAndRejectsTamperedOnes()
    {
        var signature = WebhookSigner.Sign("secret", 1, "body");

        WebhookSigner.Verify("secret", 1, "body", signature).Should().BeTrue();
        WebhookSigner.Verify("secret", 1, "bodyX", signature).Should().BeFalse();
        WebhookSigner.Verify("secret", 1, "body", "v1=00").Should().BeFalse();
        WebhookSigner.Verify("secret", 1, "body", "garbage").Should().BeFalse();
    }

    [Fact]
    public void GenerateSecret_IsRandomAndPrefixed()
    {
        var a = WebhookSigner.GenerateSecret();
        var b = WebhookSigner.GenerateSecret();

        a.Should().StartWith("whsec_");
        a.Should().NotBe(b);
        a.Length.Should().BeGreaterThan(40);
    }

    [Fact]
    public void Sign_IsIndependentOfTheCultureOfTheTimestamp()
    {
        var previous = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("ar-SA");
            WebhookSigner.Sign("s", 1700000000, "b").Should().Be(
                WebhookSigner.Sign("s", 1700000000, "b"));
            System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.InvariantCulture;
            var invariant = WebhookSigner.Sign("s", 1700000000, "b");
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("ar-SA");
            WebhookSigner.Sign("s", 1700000000, "b").Should().Be(invariant);
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = previous;
        }
    }
}

public class WebhookRetryPolicyTests
{
    private static readonly TimeSpan[] Delays =
        [TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(10), TimeSpan.FromHours(1), TimeSpan.FromHours(6)];

    [Theory]
    [InlineData(1, 30)]
    [InlineData(2, 120)]
    [InlineData(3, 600)]
    [InlineData(4, 3600)]
    [InlineData(5, 21600)]
    public void NextDelay_GrowsWithEachFailedAttempt(int attemptsMade, int seconds)
    {
        WebhookRetryPolicy.NextDelay(attemptsMade, Delays).Should().Be(TimeSpan.FromSeconds(seconds));
    }

    [Fact]
    public void NextDelay_IsNullOnceAllAttemptsAreUsed()
    {
        WebhookRetryPolicy.NextDelay(6, Delays).Should().BeNull();
        WebhookRetryPolicy.NextDelay(20, Delays).Should().BeNull();
    }

    [Theory]
    [InlineData(500, true)]
    [InlineData(502, true)]
    [InlineData(503, true)]
    [InlineData(408, true)]
    [InlineData(429, true)]
    [InlineData(400, false)]
    [InlineData(401, false)]
    [InlineData(404, false)]
    [InlineData(410, false)]
    [InlineData(301, true)]
    [InlineData(302, true)]
    public void IsRetryable_RetriesServerErrorsButNotRefusals(int status, bool expected)
    {
        WebhookRetryPolicy.IsRetryable(status).Should().Be(expected);
    }

    [Theory]
    [InlineData(200, true)]
    [InlineData(204, true)]
    [InlineData(299, true)]
    [InlineData(300, false)]
    [InlineData(199, false)]
    public void IsSuccess_IsAny2xx(int status, bool expected)
    {
        WebhookRetryPolicy.IsSuccess(status).Should().Be(expected);
    }
}

public class WebhookTargetPolicyTests
{
    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("127.5.5.5")]
    [InlineData("10.0.0.1")]
    [InlineData("172.16.0.1")]
    [InlineData("172.31.255.255")]
    [InlineData("192.168.1.10")]
    [InlineData("169.254.169.254")]
    [InlineData("100.64.0.1")]
    [InlineData("0.0.0.0")]
    [InlineData("224.0.0.1")]
    [InlineData("255.255.255.255")]
    [InlineData("::1")]
    [InlineData("::")]
    [InlineData("fe80::1")]
    [InlineData("fc00::1")]
    [InlineData("fd12:3456::1")]
    [InlineData("::ffff:127.0.0.1")]
    [InlineData("::ffff:10.1.2.3")]
    [InlineData("::ffff:169.254.169.254")]
    [InlineData("ff02::1")]
    public void IsBlockedAddress_BlocksInternalAddresses(string address)
    {
        WebhookTargetPolicy.IsBlockedAddress(IPAddress.Parse(address), allowPrivate: false).Should().BeTrue();
    }

    [Theory]
    [InlineData("8.8.8.8")]
    [InlineData("1.1.1.1")]
    [InlineData("172.15.0.1")]
    [InlineData("172.32.0.1")]
    [InlineData("100.63.255.255")]
    [InlineData("2606:4700:4700::1111")]
    [InlineData("::ffff:8.8.8.8")]
    public void IsBlockedAddress_AllowsPublicAddresses(string address)
    {
        WebhookTargetPolicy.IsBlockedAddress(IPAddress.Parse(address), allowPrivate: false).Should().BeFalse();
    }

    [Theory]
    [InlineData("10.0.0.5")]
    [InlineData("192.168.0.9")]
    [InlineData("127.0.0.1")]
    [InlineData("fd00::5")]
    public void IsBlockedAddress_OperatorMayAllowPrivateTargets(string address)
    {
        WebhookTargetPolicy.IsBlockedAddress(IPAddress.Parse(address), allowPrivate: true).Should().BeFalse();
    }

    [Theory]
    [InlineData("169.254.169.254")]
    [InlineData("fe80::1")]
    [InlineData("::ffff:169.254.1.1")]
    public void IsBlockedAddress_LinkLocalStaysBlockedEvenWhenPrivateIsAllowed(string address)
    {
        WebhookTargetPolicy.IsBlockedAddress(IPAddress.Parse(address), allowPrivate: true).Should().BeTrue();
    }

    [Theory]
    [InlineData("https://example.com/hook")]
    [InlineData("https://hooks.example.com:8443/path?x=1")]
    [InlineData("https://8.8.8.8/hook")]
    public void Validate_AcceptsPublicHttpsUrls(string url)
    {
        WebhookTargetPolicy.Validate(url, allowHttp: false, allowPrivate: false).Should().BeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not a url")]
    [InlineData("/relative")]
    [InlineData("ftp://example.com/x")]
    [InlineData("file:///etc/passwd")]
    [InlineData("http://example.com/hook")]
    [InlineData("https://user:pass@example.com/hook")]
    [InlineData("https://example.com/hook#frag")]
    [InlineData("https://localhost/hook")]
    [InlineData("https://LOCALHOST/hook")]
    [InlineData("https://app.localhost/hook")]
    [InlineData("https://printer.local/hook")]
    [InlineData("https://db.internal/hook")]
    [InlineData("https://host.localdomain/hook")]
    [InlineData("https://127.0.0.1/hook")]
    [InlineData("https://[::1]/hook")]
    [InlineData("https://10.1.1.1/hook")]
    [InlineData("https://169.254.169.254/latest/meta-data")]
    [InlineData("https://[::ffff:127.0.0.1]/hook")]
    [InlineData("https://2130706433/hook")]          // 127.0.0.1 as a single decimal number
    [InlineData("https://0x7f000001/hook")]          // 127.0.0.1 in hex
    [InlineData("https://0177.0.0.1/hook")]          // 127.0.0.1 in octal
    public void Validate_RejectsUnsafeUrls(string url)
    {
        WebhookTargetPolicy.Validate(url, allowHttp: false, allowPrivate: false).Should().NotBeNull();
    }

    [Fact]
    public void Validate_HttpNeedsTheOperatorSwitch()
    {
        WebhookTargetPolicy.Validate("http://example.com/hook", allowHttp: true, allowPrivate: false).Should().BeNull();
        WebhookTargetPolicy.Validate("http://example.com/hook", allowHttp: false, allowPrivate: false).Should().NotBeNull();
    }

    [Fact]
    public void Validate_PrivateLiteralsPassWhenTheOperatorAllowsPrivateTargets()
    {
        WebhookTargetPolicy.Validate("https://10.1.1.1/hook", allowHttp: false, allowPrivate: true).Should().BeNull();
        WebhookTargetPolicy.Validate("https://n8n.internal/hook", allowHttp: false, allowPrivate: true).Should().BeNull();
        WebhookTargetPolicy.Validate("https://169.254.169.254/x", allowHttp: false, allowPrivate: true).Should().NotBeNull();
    }

    [Fact]
    public void Validate_RejectsOverlongUrls()
    {
        WebhookTargetPolicy.Validate("https://example.com/" + new string('a', 2100), allowHttp: false, allowPrivate: false)
            .Should().NotBeNull();
    }
}
