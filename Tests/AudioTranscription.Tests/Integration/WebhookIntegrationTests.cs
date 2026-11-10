using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AudioTranscription.Api.Automation;
using AudioTranscription.Domain.Automation;
using AudioTranscription.Domain.Entities;
using AudioTranscription.Domain.Enums;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AudioTranscription.Tests.Integration;

/// <summary>S28-T3 (ADR 0009): webhooks with signature, retries, delivery log and SSRF protection.</summary>
public class WebhookIntegrationTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private const string Url = "https://hooks.example.com/transcripts";

    private readonly AutomationHost _host;

    public WebhookIntegrationTests(WebApplicationFactory<Program> factory)
    {
        _host = new AutomationHost(factory);
    }

    public void Dispose() => _host.Dispose();

    private async Task<CreatedWebhookDto> CreateAsync(string userId, string url = Url, string[]? events = null)
    {
        var response = await _host.As(userId).PostAsJsonAsync("/api/webhooks",
            new SaveWebhookRequest(url, events ?? [WebhookEvents.JobCompleted, WebhookEvents.JobFailed], null));
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<CreatedWebhookDto>())!;
    }

    private async Task<WebhookDeliveryDto> TestAsync(string userId, Guid id)
    {
        var response = await _host.As(userId).PostAsync($"/api/webhooks/{id}/test", null);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<WebhookDeliveryDto>())!;
    }

    private async Task<WebhookDeliveryStatus> StatusOfAsync(Guid deliveryId) =>
        (await _host.WithDbAsync(db => db.WebhookDeliveries.SingleAsync(d => d.Id == deliveryId))).Status;

    private async Task ProcessDueAsync()
    {
        using var scope = _host.Factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<WebhookDeliveryProcessor>().ProcessDueAsync(CancellationToken.None);
    }

    private async Task EnqueueAsync(Guid jobId, string eventName)
    {
        using var scope = _host.Factory.Services.CreateScope();
        var job = await scope.ServiceProvider.GetRequiredService<AudioTranscription.Infrastructure.Data.AppDbContext>().AudioJobs.SingleAsync(j => j.Id == jobId);
        await scope.ServiceProvider.GetRequiredService<IWebhookDispatcher>().EnqueueAsync(job, eventName, CancellationToken.None);
    }

    // --- management ----------------------------------------------------------

    [Fact]
    public async Task Create_ReturnsTheSecretOnce_AndStoresItEncrypted()
    {
        var created = await CreateAsync(_host.Alice);

        created.Secret.Should().StartWith("whsec_");
        created.Url.Should().Be(Url);
        created.IsActive.Should().BeTrue();

        var stored = await _host.WithDbAsync(db => db.Webhooks.SingleAsync());
        stored.ProtectedSecret.Should().NotBeNullOrEmpty().And.NotContain(created.Secret);
        (await _host.As(_host.Alice).GetStringAsync("/api/webhooks")).Should().NotContain(created.Secret).And.NotContain("rotectedSecret");
    }

    [Fact]
    public async Task List_Update_Delete_AreRestrictedToTheOwner()
    {
        var created = await CreateAsync(_host.Alice);

        (await _host.As(_host.Bob).GetFromJsonAsync<List<WebhookDto>>("/api/webhooks")).Should().BeEmpty();
        var foreignUpdate = await _host.As(_host.Bob).PutAsJsonAsync($"/api/webhooks/{created.Id}", new SaveWebhookRequest(Url, [WebhookEvents.JobCompleted], null));
        foreignUpdate.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await _host.As(_host.Bob).DeleteAsync($"/api/webhooks/{created.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await _host.As(_host.Bob).PostAsync($"/api/webhooks/{created.Id}/test", null)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await _host.As(_host.Bob).GetAsync($"/api/webhooks/{created.Id}/deliveries")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        var update = await _host.As(_host.Alice).PutAsJsonAsync($"/api/webhooks/{created.Id}",
            new SaveWebhookRequest("https://other.example.com/x", [WebhookEvents.JobFailed], false));
        update.StatusCode.Should().Be(HttpStatusCode.OK);
        var dto = (await update.Content.ReadFromJsonAsync<WebhookDto>())!;
        dto.Url.Should().Be("https://other.example.com/x");
        dto.Events.Should().Equal(WebhookEvents.JobFailed);
        dto.IsActive.Should().BeFalse();

        (await _host.As(_host.Alice).DeleteAsync($"/api/webhooks/{created.Id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await _host.As(_host.Alice).GetFromJsonAsync<List<WebhookDto>>("/api/webhooks")).Should().BeEmpty();
    }

    [Fact]
    public async Task Update_KeepsTheSecret_SoReceiversNeedNoChange()
    {
        var created = await CreateAsync(_host.Alice);
        await _host.As(_host.Alice).PutAsJsonAsync($"/api/webhooks/{created.Id}", new SaveWebhookRequest("https://b.example.com/x", [WebhookEvents.JobCompleted], null));

        await TestAsync(_host.Alice, created.Id);

        var request = _host.Receiver.Requests.Single();
        request.Uri.Host.Should().Be("b.example.com");
        WebhookSigner.Verify(created.Secret, long.Parse(request.Headers["X-Webhook-Timestamp"]), request.Body, request.Headers["X-Webhook-Signature"]).Should().BeTrue();
    }

    [Theory]
    [InlineData("http://hooks.example.com/x")]
    [InlineData("https://localhost/x")]
    [InlineData("https://127.0.0.1/x")]
    [InlineData("https://10.0.0.5/x")]
    [InlineData("https://192.168.1.1/x")]
    [InlineData("https://169.254.169.254/latest/meta-data/")]
    [InlineData("https://[::1]/x")]
    [InlineData("https://user:pw@hooks.example.com/x")]
    [InlineData("https://db.internal/x")]
    [InlineData("ftp://hooks.example.com/x")]
    [InlineData("")]
    public async Task Create_RejectsUnsafeTargets(string url)
    {
        var response = await _host.As(_host.Alice).PostAsJsonAsync("/api/webhooks", new SaveWebhookRequest(url, [WebhookEvents.JobCompleted], null));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await _host.WithDbAsync(db => db.Webhooks.CountAsync())).Should().Be(0);
    }

    [Fact]
    public async Task Update_RejectsUnsafeTargetsToo()
    {
        var created = await CreateAsync(_host.Alice);

        var response = await _host.As(_host.Alice).PutAsJsonAsync($"/api/webhooks/{created.Id}",
            new SaveWebhookRequest("https://169.254.169.254/", [WebhookEvents.JobCompleted], null));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await _host.WithDbAsync(db => db.Webhooks.SingleAsync())).Url.Should().Be(Url);
    }

    [Theory]
    [InlineData("job.completed", "job.completed")]
    [InlineData("nope")]
    [InlineData("webhook.test")]
    public async Task Create_RejectsInvalidEvents(params string[] events)
    {
        var response = await _host.As(_host.Alice).PostAsJsonAsync("/api/webhooks", new SaveWebhookRequest(Url, events, null));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Create_AllowsAtMostTenWebhooksPerUser()
    {
        for (var i = 0; i < 10; i++)
            await CreateAsync(_host.Alice, $"https://hooks.example.com/{i}");

        var response = await _host.As(_host.Alice).PostAsJsonAsync("/api/webhooks", new SaveWebhookRequest(Url, [WebhookEvents.JobCompleted], null));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task DeletingAWebhook_RemovesItsDeliveries()
    {
        var created = await CreateAsync(_host.Alice);
        await TestAsync(_host.Alice, created.Id);

        await _host.As(_host.Alice).DeleteAsync($"/api/webhooks/{created.Id}");

        (await _host.WithDbAsync(db => db.WebhookDeliveries.CountAsync())).Should().Be(0);
    }

    // --- test delivery and signature ---------------------------------------------

    [Fact]
    public async Task TestEndpoint_SendsASignedRequest_AndLogsTheDelivery()
    {
        var created = await CreateAsync(_host.Alice);

        var delivery = await TestAsync(_host.Alice, created.Id);

        delivery.Status.Should().Be("succeeded");
        delivery.Event.Should().Be(WebhookEvents.Test);
        delivery.Attempts.Should().Be(1);
        delivery.LastStatusCode.Should().Be(200);

        var request = _host.Receiver.Requests.Should().ContainSingle().Subject;
        request.Method.Should().Be(HttpMethod.Post);
        request.Uri.ToString().Should().Be(Url);
        request.Headers["X-Webhook-Id"].Should().Be(delivery.Id.ToString());
        request.Headers["X-Webhook-Event"].Should().Be(WebhookEvents.Test);
        request.Headers["Content-Type"].Should().StartWith("application/json");
        var timestamp = long.Parse(request.Headers["X-Webhook-Timestamp"]);
        timestamp.Should().Be(_host.Clock.GetUtcNow().ToUnixTimeSeconds());
        request.Headers["X-Webhook-Signature"].Should().StartWith("v1=");
        WebhookSigner.Verify(created.Secret, timestamp, request.Body, request.Headers["X-Webhook-Signature"]).Should().BeTrue();
        WebhookSigner.Verify("whsec_wrong", timestamp, request.Body, request.Headers["X-Webhook-Signature"]).Should().BeFalse();

        using var body = JsonDocument.Parse(request.Body);
        body.RootElement.GetProperty("event").GetString().Should().Be(WebhookEvents.Test);
        body.RootElement.GetProperty("deliveryId").GetGuid().Should().Be(delivery.Id);
    }

    [Fact]
    public async Task TestEndpoint_MakesExactlyOneAttempt_AndReportsTheFailure()
    {
        var created = await CreateAsync(_host.Alice);
        _host.Receiver.Respond(HttpStatusCode.InternalServerError);

        var delivery = await TestAsync(_host.Alice, created.Id);

        delivery.Status.Should().Be("failed");
        delivery.LastStatusCode.Should().Be(500);
        delivery.LastError.Should().Be("http 500");
        _host.Receiver.Requests.Should().HaveCount(1);
    }

    [Fact]
    public async Task TestEndpoint_IsRateLimitedPerUser()
    {
        var created = await CreateAsync(_host.Alice);
        var other = await CreateAsync(_host.Bob);

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 12; i++)
            statuses.Add((await _host.As(_host.Alice).PostAsync($"/api/webhooks/{created.Id}/test", null)).StatusCode);

        statuses.Take(10).Should().OnlyContain(s => s == HttpStatusCode.OK);
        statuses.Skip(10).Should().OnlyContain(s => s == HttpStatusCode.TooManyRequests);
        (await _host.As(_host.Bob).PostAsync($"/api/webhooks/{other.Id}/test", null)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Deliveries_ListsTheHistoryNewestFirst()
    {
        var created = await CreateAsync(_host.Alice);
        var first = await TestAsync(_host.Alice, created.Id);
        _host.Clock.Advance(TimeSpan.FromMinutes(1));
        var second = await TestAsync(_host.Alice, created.Id);

        var history = await _host.As(_host.Alice).GetFromJsonAsync<List<WebhookDeliveryDto>>($"/api/webhooks/{created.Id}/deliveries");

        history!.Select(d => d.Id).Should().Equal(second.Id, first.Id);
    }

    // --- events and retries ---------------------------------------------------------

    [Fact]
    public async Task ACompletedJob_NotifiesTheOwnersMatchingActiveWebhooksOnly()
    {
        await CreateAsync(_host.Alice, "https://a.example.com/x", [WebhookEvents.JobFailed]);
        var both = await CreateAsync(_host.Alice, "https://b.example.com/x");
        var inactive = await CreateAsync(_host.Alice, "https://c.example.com/x");
        await _host.As(_host.Alice).PutAsJsonAsync($"/api/webhooks/{inactive.Id}", new SaveWebhookRequest("https://c.example.com/x", [WebhookEvents.JobCompleted], false));
        await CreateAsync(_host.Bob, "https://d.example.com/x");
        var jobId = await _host.AddJobAsync(_host.Alice);

        await EnqueueAsync(jobId, WebhookEvents.JobCompleted);

        var deliveries = await _host.WithDbAsync(db => db.WebhookDeliveries.ToListAsync());
        deliveries.Should().ContainSingle().Which.WebhookId.Should().Be(both.Id);
        deliveries[0].Status.Should().Be(WebhookDeliveryStatus.Pending);
        deliveries[0].JobId.Should().Be(jobId);
    }

    [Fact]
    public async Task ThePayload_CarriesNoTranscriptAndNoErrorDetails()
    {
        await CreateAsync(_host.Alice);
        var failed = await _host.AddJobAsync(_host.Alice, "private-name.mp3", AudioJobStatus.Failed);

        await EnqueueAsync(failed, WebhookEvents.JobFailed);
        await ProcessDueAsync();

        var body = _host.Receiver.Requests.Single().Body;
        body.Should().NotContain("geheimer Inhalt").And.NotContain("interne Details");
        using var json = JsonDocument.Parse(body);
        var data = json.RootElement.GetProperty("data");
        data.GetProperty("jobId").GetGuid().Should().Be(failed);
        data.GetProperty("fileName").GetString().Should().Be("private-name.mp3");
        data.GetProperty("status").GetString().Should().Be("Failed");
        json.RootElement.GetProperty("event").GetString().Should().Be(WebhookEvents.JobFailed);
    }

    [Fact]
    public async Task AFailedDelivery_IsRetriedWithGrowingDelays_UntilItSucceeds()
    {
        await CreateAsync(_host.Alice);
        var jobId = await _host.AddJobAsync(_host.Alice);
        _host.Receiver.Respond(HttpStatusCode.BadGateway);
        _host.Receiver.Respond(HttpStatusCode.ServiceUnavailable);
        await EnqueueAsync(jobId, WebhookEvents.JobCompleted);

        await ProcessDueAsync();
        var delivery = await _host.WithDbAsync(db => db.WebhookDeliveries.SingleAsync());
        delivery.Status.Should().Be(WebhookDeliveryStatus.Pending);
        delivery.Attempts.Should().Be(1);
        delivery.LastStatusCode.Should().Be(502);
        delivery.NextAttemptAtUtc.Should().Be(_host.Clock.GetUtcNow().UtcDateTime.AddSeconds(30));

        await ProcessDueAsync();                                 // not due yet
        _host.Receiver.Requests.Should().HaveCount(1);

        _host.Clock.Advance(TimeSpan.FromSeconds(30));
        await ProcessDueAsync();
        delivery = await _host.WithDbAsync(db => db.WebhookDeliveries.SingleAsync());
        delivery.Attempts.Should().Be(2);
        delivery.NextAttemptAtUtc.Should().Be(_host.Clock.GetUtcNow().UtcDateTime.AddMinutes(2));

        _host.Clock.Advance(TimeSpan.FromMinutes(2));
        await ProcessDueAsync();
        delivery = await _host.WithDbAsync(db => db.WebhookDeliveries.SingleAsync());
        delivery.Status.Should().Be(WebhookDeliveryStatus.Succeeded);
        delivery.Attempts.Should().Be(3);
        delivery.CompletedAtUtc.Should().NotBeNull();
        _host.Receiver.Requests.Select(r => r.Headers["X-Webhook-Id"]).Distinct().Should().ContainSingle("every attempt carries the same delivery id");
    }

    [Fact]
    public async Task ADeliveryThatKeepsFailing_GivesUpAfterTheLastAttempt()
    {
        await CreateAsync(_host.Alice);
        var jobId = await _host.AddJobAsync(_host.Alice);
        for (var i = 0; i < 10; i++)
            _host.Receiver.Respond(HttpStatusCode.InternalServerError);
        await EnqueueAsync(jobId, WebhookEvents.JobCompleted);

        for (var i = 0; i < 12; i++)
        {
            await ProcessDueAsync();
            _host.Clock.Advance(TimeSpan.FromHours(7));
        }

        var delivery = await _host.WithDbAsync(db => db.WebhookDeliveries.SingleAsync());
        delivery.Status.Should().Be(WebhookDeliveryStatus.Failed);
        delivery.Attempts.Should().Be(6);
        delivery.LastError.Should().Be("http 500");
        _host.Receiver.Requests.Should().HaveCount(6);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task ARefusal_IsNotRepeated(HttpStatusCode status)
    {
        await CreateAsync(_host.Alice);
        var jobId = await _host.AddJobAsync(_host.Alice);
        _host.Receiver.Respond(status);
        await EnqueueAsync(jobId, WebhookEvents.JobCompleted);

        await ProcessDueAsync();
        _host.Clock.Advance(TimeSpan.FromDays(1));
        await ProcessDueAsync();

        var delivery = await _host.WithDbAsync(db => db.WebhookDeliveries.SingleAsync());
        delivery.Status.Should().Be(WebhookDeliveryStatus.Failed);
        delivery.Attempts.Should().Be(1);
    }

    [Fact]
    public async Task ARedirect_IsNotFollowed_AndCountsAsAFailure()
    {
        await CreateAsync(_host.Alice);
        var jobId = await _host.AddJobAsync(_host.Alice);
        _host.Receiver.Redirect("https://127.0.0.1/internal");
        await EnqueueAsync(jobId, WebhookEvents.JobCompleted);

        await ProcessDueAsync();

        _host.Receiver.Requests.Should().HaveCount(1);
        var delivery = await _host.WithDbAsync(db => db.WebhookDeliveries.SingleAsync());
        delivery.Status.Should().Be(WebhookDeliveryStatus.Pending);
        delivery.LastStatusCode.Should().Be(302);
        delivery.LastError.Should().Be("http 302");
    }

    [Fact]
    public async Task ATimeoutOrConnectionError_IsRetried_WithoutLeakingDetails()
    {
        await CreateAsync(_host.Alice);
        var jobId = await _host.AddJobAsync(_host.Alice);
        _host.Receiver.Throw(new TaskCanceledException("secret detail 10.1.2.3"));
        _host.Receiver.Throw(new HttpRequestException("Name or service not known: internal-host.corp"));
        await EnqueueAsync(jobId, WebhookEvents.JobCompleted);

        await ProcessDueAsync();
        var first = await _host.WithDbAsync(db => db.WebhookDeliveries.SingleAsync());
        first.LastError.Should().Be("timeout");
        first.Status.Should().Be(WebhookDeliveryStatus.Pending);

        _host.Clock.Advance(TimeSpan.FromMinutes(1));
        await ProcessDueAsync();
        var second = await _host.WithDbAsync(db => db.WebhookDeliveries.SingleAsync());
        second.LastError.Should().Be("connection error");
        second.Attempts.Should().Be(2);
    }

    [Fact]
    public async Task APendingDelivery_SurvivesARestart_BecauseItLivesInTheDatabase()
    {
        await CreateAsync(_host.Alice);
        var jobId = await _host.AddJobAsync(_host.Alice);
        await EnqueueAsync(jobId, WebhookEvents.JobCompleted);

        // a fresh scope (as after a restart) finds and sends it
        await ProcessDueAsync();

        (await _host.WithDbAsync(db => db.WebhookDeliveries.SingleAsync())).Status.Should().Be(WebhookDeliveryStatus.Succeeded);
    }

    [Fact]
    public async Task ADeliveryToAWebhookThatWasDeactivatedMeanwhile_IsNotSent()
    {
        var created = await CreateAsync(_host.Alice);
        var jobId = await _host.AddJobAsync(_host.Alice);
        await EnqueueAsync(jobId, WebhookEvents.JobCompleted);
        await _host.As(_host.Alice).PutAsJsonAsync($"/api/webhooks/{created.Id}", new SaveWebhookRequest(Url, [WebhookEvents.JobCompleted], false));

        await ProcessDueAsync();

        _host.Receiver.Requests.Should().BeEmpty();
        (await _host.WithDbAsync(db => db.WebhookDeliveries.SingleAsync())).Status.Should().Be(WebhookDeliveryStatus.Failed);
    }

    [Fact]
    public async Task ASecretThatCannotBeDecrypted_FailsTheDeliveryForGood_WithoutSending()
    {
        await CreateAsync(_host.Alice);
        var jobId = await _host.AddJobAsync(_host.Alice);
        await _host.WithDbAsync(async db =>
        {
            (await db.Webhooks.SingleAsync()).ProtectedSecret = "not-a-protected-payload";
            await db.SaveChangesAsync();
        });
        await EnqueueAsync(jobId, WebhookEvents.JobCompleted);

        await ProcessDueAsync();

        _host.Receiver.Requests.Should().BeEmpty();
        var delivery = await _host.WithDbAsync(db => db.WebhookDeliveries.SingleAsync());
        delivery.Status.Should().Be(WebhookDeliveryStatus.Failed);
        delivery.LastError.Should().Be("secret unreadable");
    }

    [Fact]
    public async Task OldDeliveries_AreRemoved()
    {
        var created = await CreateAsync(_host.Alice);
        await TestAsync(_host.Alice, created.Id);
        _host.Clock.Advance(TimeSpan.FromDays(31));

        using (var scope = _host.Factory.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<WebhookDeliveryProcessor>().PruneAsync(CancellationToken.None);

        (await _host.WithDbAsync(db => db.WebhookDeliveries.CountAsync())).Should().Be(0);
    }

    [Fact]
    public async Task WebhookEndpoints_RequireASignedInUser()
    {
        var client = _host.Factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, TestAuthHandler.NoUser);

        (await client.GetAsync("/api/webhooks")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await client.PostAsJsonAsync("/api/webhooks", new SaveWebhookRequest(Url, [WebhookEvents.JobCompleted], null))).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}

/// <summary>The checks that connect time adds to the URL validation (DNS rebinding, ADR 0009).</summary>
public class SsrfSafeConnectTests
{
    private static Func<string, CancellationToken, Task<IPAddress[]>> Resolver(params string[] addresses) =>
        (_, _) => Task.FromResult(addresses.Select(IPAddress.Parse).ToArray());

    [Theory]
    [InlineData("10.0.0.1")]
    [InlineData("127.0.0.1")]
    [InlineData("169.254.169.254")]
    [InlineData("::1")]
    public async Task ANameThatResolvesToAnInternalAddress_IsRefused(string resolved)
    {
        var act = () => SsrfSafeConnect.ResolveAsync("rebind.example.com", allowPrivate: false, Resolver(resolved), CancellationToken.None);

        await act.Should().ThrowAsync<WebhookTargetBlockedException>();
    }

    [Fact]
    public async Task OneInternalAddressAmongPublicOnes_RefusesTheWholeName()
    {
        var act = () => SsrfSafeConnect.ResolveAsync("mixed.example.com", false, Resolver("8.8.8.8", "10.0.0.1"), CancellationToken.None);

        await act.Should().ThrowAsync<WebhookTargetBlockedException>();
    }

    [Fact]
    public async Task APublicName_IsResolvedToItsAddresses()
    {
        var addresses = await SsrfSafeConnect.ResolveAsync("ok.example.com", false, Resolver("8.8.8.8", "1.1.1.1"), CancellationToken.None);

        addresses.Select(a => a.ToString()).Should().Equal("8.8.8.8", "1.1.1.1");
    }

    [Fact]
    public async Task AnInternalLiteral_IsRefusedWithoutAskingDns()
    {
        var asked = false;
        var act = () => SsrfSafeConnect.ResolveAsync("127.0.0.1", false, (_, _) => { asked = true; return Task.FromResult(Array.Empty<IPAddress>()); }, CancellationToken.None);

        await act.Should().ThrowAsync<WebhookTargetBlockedException>();
        asked.Should().BeFalse();
    }

    [Fact]
    public async Task APrivateTarget_IsAllowedOnlyWhenTheOperatorSaysSo()
    {
        var addresses = await SsrfSafeConnect.ResolveAsync("n8n.lan", allowPrivate: true, Resolver("192.168.1.20"), CancellationToken.None);
        addresses.Should().ContainSingle();

        var metadata = () => SsrfSafeConnect.ResolveAsync("n8n.lan", true, Resolver("169.254.169.254"), CancellationToken.None);
        await metadata.Should().ThrowAsync<WebhookTargetBlockedException>();
    }

    [Fact]
    public async Task ANameWithoutAddresses_IsRefused()
    {
        var act = () => SsrfSafeConnect.ResolveAsync("nothing.example.com", false, Resolver(), CancellationToken.None);

        await act.Should().ThrowAsync<WebhookTargetBlockedException>();
    }
}
