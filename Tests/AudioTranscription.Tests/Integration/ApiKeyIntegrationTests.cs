using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AudioTranscription.Api.Automation;
using AudioTranscription.Domain.Automation;
using AudioTranscription.Domain.Entities;
using AudioTranscription.Domain.Enums;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AudioTranscription.Tests.Integration;

/// <summary>S28-T2 (ADR 0009): API keys for scripts.</summary>
public class ApiKeyIntegrationTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private readonly AutomationHost _host;

    public ApiKeyIntegrationTests(WebApplicationFactory<Program> factory)
    {
        _host = new AutomationHost(factory, configure: services =>
            services.Configure<ApiKeyOptions>(o => o.RequestsPerMinute = 30));
    }

    public void Dispose() => _host.Dispose();

    private async Task<CreatedApiKeyDto> CreateKeyAsync(string userId, string[]? scopes = null, DateTime? expiresAt = null, string name = "CI")
    {
        var response = await _host.As(userId).PostAsJsonAsync("/api/api-keys",
            new CreateApiKeyRequest(name, scopes ?? [ApiKeyScopes.JobsRead, ApiKeyScopes.JobsWrite], expiresAt));
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<CreatedApiKeyDto>())!;
    }

    // --- management ----------------------------------------------------------

    [Fact]
    public async Task Create_ReturnsTheKeyExactlyOnce_AndStoresOnlyItsHash()
    {
        var created = await CreateKeyAsync(_host.Alice);

        created.Key.Should().StartWith("at_" + created.Prefix + "_");
        created.Scopes.Should().BeEquivalentTo([ApiKeyScopes.JobsRead, ApiKeyScopes.JobsWrite]);

        var stored = await _host.WithDbAsync(db => db.ApiKeys.SingleAsync());
        stored.OwnerId.Should().Be(_host.Alice);
        stored.KeyHash.Should().Equal(ApiKeyToken.HashOf(created.Key));
        stored.Prefix.Should().Be(created.Prefix);
        JsonSerializer.Serialize(stored).Should().NotContain(created.Key);

        var listJson = await _host.As(_host.Alice).GetStringAsync("/api/api-keys");
        listJson.Should().NotContain(created.Key).And.NotContain(created.Key[^20..]);
        listJson.Should().Contain(created.Prefix).And.Contain("\"name\":\"CI\"");
        listJson.ToLowerInvariant().Should().NotContain("hash");
    }

    [Fact]
    public async Task List_ShowsOnlyTheOwnKeys()
    {
        await CreateKeyAsync(_host.Alice, name: "alice-key");
        await CreateKeyAsync(_host.Bob, name: "bob-key");

        var keys = await _host.As(_host.Alice).GetFromJsonAsync<List<ApiKeyDto>>("/api/api-keys");

        keys.Should().ContainSingle().Which.Name.Should().Be("alice-key");
    }

    [Fact]
    public async Task Delete_RevokesTheKey_AndNobodyElseCanDeleteIt()
    {
        var created = await CreateKeyAsync(_host.Alice);

        (await _host.As(_host.Bob).DeleteAsync($"/api/api-keys/{created.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await _host.WithKey(created.Key).GetAsync("/api/audio-jobs")).StatusCode.Should().Be(HttpStatusCode.OK);

        (await _host.As(_host.Alice).DeleteAsync($"/api/api-keys/{created.Id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await _host.WithKey(created.Key).GetAsync("/api/audio-jobs")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await _host.As(_host.Alice).DeleteAsync($"/api/api-keys/{created.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    public static TheoryData<string, string[], int> InvalidRequests => new()
    {
        { "", new[] { ApiKeyScopes.JobsRead }, 0 },
        { new string('x', 101), new[] { ApiKeyScopes.JobsRead }, 0 },
        { "ok", Array.Empty<string>(), 0 },
        { "ok", new[] { "admin" }, 0 },
        { "ok", new[] { ApiKeyScopes.JobsRead, ApiKeyScopes.JobsRead }, 0 },
        { "ok", new[] { ApiKeyScopes.JobsRead }, -1 },     // expiry in the past
        { "ok", new[] { ApiKeyScopes.JobsRead }, 366 },    // more than 365 days
    };

    [Theory]
    [MemberData(nameof(InvalidRequests))]
    public async Task Create_RejectsInvalidInput(string name, string[] scopes, int expiresInDays)
    {
        DateTime? expires = expiresInDays == 0 ? null : _host.Clock.GetUtcNow().UtcDateTime.AddDays(expiresInDays);

        var response = await _host.As(_host.Alice).PostAsJsonAsync("/api/api-keys", new CreateApiKeyRequest(name, scopes, expires));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Create_AllowsAtMostTwentyKeysPerUser()
    {
        for (var i = 0; i < 20; i++)
            await CreateKeyAsync(_host.Alice, name: $"k{i}");

        var response = await _host.As(_host.Alice).PostAsJsonAsync("/api/api-keys",
            new CreateApiKeyRequest("one too many", [ApiKeyScopes.JobsRead], null));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await CreateKeyAsync(_host.Bob)).Key.Should().NotBeNullOrEmpty();
    }

    // --- authentication ---------------------------------------------------------

    [Fact]
    public async Task ABearerKeyAuthenticatesAsItsOwner_AndSeesOnlyTheirJobs()
    {
        var aliceJob = await _host.AddJobAsync(_host.Alice, "alice.mp3");
        await _host.AddJobAsync(_host.Bob, "bob.mp3");
        var created = await CreateKeyAsync(_host.Alice);

        var response = await _host.WithKey(created.Key).GetAsync("/api/audio-jobs");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain(aliceJob.ToString()).And.NotContain("bob.mp3");
    }

    [Fact]
    public async Task TheXApiKeyHeaderWorksToo()
    {
        var created = await CreateKeyAsync(_host.Alice);

        (await _host.WithKey(created.Key, asHeader: true).GetAsync("/api/audio-jobs")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task AKeyCannotReachAnotherUsersJob()
    {
        var bobJob = await _host.AddJobAsync(_host.Bob);
        var created = await CreateKeyAsync(_host.Alice);

        (await _host.WithKey(created.Key).GetAsync($"/api/audio-jobs/{bobJob}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData("garbage")]
    [InlineData("at_")]
    [InlineData("at_ABCDEFGH_wrongwrongwrongwrongwrongwrongwrongwrongwrong")]
    public async Task UnknownOrMalformedKeys_Get401(string key)
    {
        var response = await _host.WithKey(key, asHeader: true).GetAsync("/api/audio-jobs");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AKeyWithTheRightPrefixButWrongSecret_Gets401()
    {
        var created = await CreateKeyAsync(_host.Alice);
        var forged = created.Key[..^4] + (created.Key.EndsWith("AAAA") ? "BBBB" : "AAAA");

        (await _host.WithKey(forged).GetAsync("/api/audio-jobs")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AnExpiredKey_Gets401()
    {
        var created = await CreateKeyAsync(_host.Alice, expiresAt: _host.Clock.GetUtcNow().UtcDateTime.AddDays(1));
        (await _host.WithKey(created.Key).GetAsync("/api/audio-jobs")).StatusCode.Should().Be(HttpStatusCode.OK);

        _host.Clock.Advance(TimeSpan.FromDays(2));

        (await _host.WithKey(created.Key).GetAsync("/api/audio-jobs")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task TheKeyOfADeactivatedUser_Gets401()
    {
        var created = await CreateKeyAsync(_host.Alice);
        using (var scope = _host.Factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
            var alice = (await users.FindByIdAsync(_host.Alice))!;
            await users.SetLockoutEnabledAsync(alice, true);
            await users.SetLockoutEndDateAsync(alice, DateTimeOffset.MaxValue);
        }

        (await _host.WithKey(created.Key).GetAsync("/api/audio-jobs")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task UsingAKey_RecordsLastUsed_AtMostOncePerMinute()
    {
        var created = await CreateKeyAsync(_host.Alice);
        var client = _host.WithKey(created.Key);

        await client.GetAsync("/api/audio-jobs");
        var first = (await _host.WithDbAsync(db => db.ApiKeys.SingleAsync())).LastUsedAtUtc;
        first.Should().Be(_host.Clock.GetUtcNow().UtcDateTime);

        _host.Clock.Advance(TimeSpan.FromSeconds(20));
        await client.GetAsync("/api/audio-jobs");
        (await _host.WithDbAsync(db => db.ApiKeys.SingleAsync())).LastUsedAtUtc.Should().Be(first);

        _host.Clock.Advance(TimeSpan.FromSeconds(60));
        await client.GetAsync("/api/audio-jobs");
        (await _host.WithDbAsync(db => db.ApiKeys.SingleAsync())).LastUsedAtUtc.Should().Be(_host.Clock.GetUtcNow().UtcDateTime);
    }

    // --- scopes -----------------------------------------------------------------

    [Fact]
    public async Task AReadOnlyKey_CanRead_ButNotChange()
    {
        var job = await _host.AddJobAsync(_host.Alice);
        var created = await CreateKeyAsync(_host.Alice, [ApiKeyScopes.JobsRead]);
        var client = _host.WithKey(created.Key);

        (await client.GetAsync($"/api/audio-jobs/{job}")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.DeleteAsync($"/api/audio-jobs/{job}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await _host.WithDbAsync(db => db.AudioJobs.AnyAsync(j => j.Id == job))).Should().BeTrue();
    }

    [Fact]
    public async Task AWriteOnlyKey_CanChange_ButNotRead()
    {
        var job = await _host.AddJobAsync(_host.Alice);
        var created = await CreateKeyAsync(_host.Alice, [ApiKeyScopes.JobsWrite]);
        var client = _host.WithKey(created.Key);

        (await client.GetAsync($"/api/audio-jobs/{job}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await client.DeleteAsync($"/api/audio-jobs/{job}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Theory]
    [InlineData("GET", "/api/api-keys")]
    [InlineData("POST", "/api/api-keys")]
    [InlineData("GET", "/api/webhooks")]
    [InlineData("POST", "/api/webhooks")]
    [InlineData("GET", "/api/admin/users")]
    [InlineData("GET", "/api/auth/me")]
    [InlineData("GET", "/api/glossaries")]
    [InlineData("GET", "/openapi/v1.json")]
    public async Task AKeyNeverReachesManagementEndpoints(string method, string path)
    {
        var created = await CreateKeyAsync(_host.Alice);
        var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (method == "POST")
            request.Content = JsonContent.Create(new { });

        var response = await _host.WithKey(created.Key).SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // --- secrets and limits --------------------------------------------------------

    [Fact]
    public async Task TheKeyNeverAppearsInTheLogs()
    {
        var created = await CreateKeyAsync(_host.Alice);
        var secret = created.Key[(created.Prefix.Length + 4)..];
        var wrong = created.Key[..^3] + "zzz";

        await _host.WithKey(created.Key).GetAsync("/api/audio-jobs");
        await _host.WithKey(wrong).GetAsync("/api/audio-jobs");
        await _host.WithKey(created.Key).DeleteAsync("/api/api-keys/" + created.Id);   // forbidden for keys
        await _host.As(_host.Alice).DeleteAsync($"/api/api-keys/{created.Id}");

        string[] logged;
        lock (_host.Logs.Messages)
            logged = [.. _host.Logs.Messages];
        logged.Should().NotBeEmpty();
        logged.Should().NotContain(m => m.Contains(secret) || m.Contains(created.Key) || m.Contains(wrong));
    }

    [Fact]
    public async Task TooManyRequestsWithOneKey_Get429()
    {
        var created = await CreateKeyAsync(_host.Alice);
        var client = _host.WithKey(created.Key);

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 35; i++)
            statuses.Add((await client.GetAsync("/api/audio-jobs")).StatusCode);

        statuses.Take(30).Should().OnlyContain(s => s == HttpStatusCode.OK);
        statuses.Skip(30).Should().Contain(HttpStatusCode.TooManyRequests);
        // Another key and the browser session are not affected
        var other = await CreateKeyAsync(_host.Bob);
        (await _host.WithKey(other.Key).GetAsync("/api/audio-jobs")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await _host.As(_host.Alice).GetAsync("/api/audio-jobs")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ApiKeyEndpoints_RequireASignedInUser()
    {
        var client = _host.Factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, TestAuthHandler.NoUser);

        (await client.GetAsync("/api/api-keys")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await client.PostAsJsonAsync("/api/api-keys", new CreateApiKeyRequest("x", [ApiKeyScopes.JobsRead], null)))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task TheKeyIsAcceptedForUploadsToo()
    {
        var created = await CreateKeyAsync(_host.Alice, [ApiKeyScopes.JobsWrite]);

        // No file in the request: it must pass authentication and scope and fail in the upload itself (400/415), not 401/403
        var response = await _host.WithKey(created.Key).PostAsync("/api/audio-jobs", new MultipartFormDataContent());

        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized).And.NotBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task AJobUploadedWithAKey_BelongsToTheKeyOwner()
    {
        var created = await CreateKeyAsync(_host.Alice, [ApiKeyScopes.JobsWrite, ApiKeyScopes.JobsRead]);
        var content = new MultipartFormDataContent();
        var file = new ByteArrayContent([0x49, 0x44, 0x33, 0x03, 0, 0, 0, 0, 0, 0x15, 1, 2, 3, 4]);
        file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("audio/mpeg");
        content.Add(file, "file", "from-script.mp3");

        var response = await _host.WithKey(created.Key).PostAsync("/api/audio-jobs", content);

        response.StatusCode.Should().BeOneOf(HttpStatusCode.Accepted, HttpStatusCode.Created, HttpStatusCode.OK);
        var job = await _host.WithDbAsync(db => db.AudioJobs.SingleAsync(j => j.FileName == "from-script.mp3"));
        job.OwnerId.Should().Be(_host.Alice);
        job.Status.Should().Be(AudioJobStatus.Pending);
    }
}
