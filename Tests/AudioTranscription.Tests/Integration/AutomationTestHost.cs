using System.Net;
using System.Text;
using AudioTranscription.Api.Automation;
using AudioTranscription.Api.BackgroundServices;
using AudioTranscription.Infrastructure.Data;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AudioTranscription.Tests.Integration;

/// <summary>A clock tests can move; everything else about <see cref="TimeProvider"/> stays the system's.</summary>
public sealed class FakeClock(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset _now = start;
    public override DateTimeOffset GetUtcNow() => _now;
    public void Advance(TimeSpan by) => _now += by;
}

/// <summary>Stands in for the webhook receiver: records every request and answers from a script.</summary>
public sealed class FakeReceiver : HttpMessageHandler
{
    public sealed record Received(HttpMethod Method, Uri Uri, Dictionary<string, string> Headers, string Body);

    private readonly Queue<Func<HttpResponseMessage>> _script = new();
    public List<Received> Requests { get; } = [];

    /// <summary>Answers used up in order; once empty every request gets 200.</summary>
    public void Respond(HttpStatusCode status) => _script.Enqueue(() => new HttpResponseMessage(status));
    public void Throw(Exception exception) => _script.Enqueue(() => throw exception);
    public void Redirect(string location) => _script.Enqueue(() =>
    {
        var response = new HttpResponseMessage(HttpStatusCode.Found);
        response.Headers.Location = new Uri(location);
        return response;
    });

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var headers = request.Headers.Concat(request.Content?.Headers ?? Enumerable.Empty<KeyValuePair<string, IEnumerable<string>>>())
            .ToDictionary(h => h.Key, h => string.Join(",", h.Value), StringComparer.OrdinalIgnoreCase);
        var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Add(new Received(request.Method, request.RequestUri!, headers, body));
        return _script.Count > 0 ? _script.Dequeue()() : new HttpResponseMessage(HttpStatusCode.OK);
    }
}

/// <summary>Collects every log message, to check that secrets never reach the logs.</summary>
public sealed class CapturingLoggerProvider : ILoggerProvider
{
    public List<string> Messages { get; } = [];
    public ILogger CreateLogger(string categoryName) => new Capture(this);
    public void Dispose() { }

    private sealed class Capture(CapturingLoggerProvider owner) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (owner.Messages)
                owner.Messages.Add(formatter(state, exception) + (exception is null ? string.Empty : "\n" + exception));
        }
    }
}

/// <summary>
/// A test application for the automation features (S28): in-memory database, test sign-in next to the real API key
/// scheme, a script-able webhook receiver, a movable clock and no background services that would race the tests.
/// </summary>
public sealed class AutomationHost : IDisposable
{
    public const string AliceEmail = "alice@example.com";
    public const string BobEmail = "bob@example.com";

    public TempDirectory Dir { get; } = new();
    public FakeClock Clock { get; } = new(new DateTimeOffset(2026, 11, 10, 12, 0, 0, TimeSpan.Zero));
    public FakeReceiver Receiver { get; } = new();
    public CapturingLoggerProvider Logs { get; } = new();
    public WebApplicationFactory<Program> Factory { get; }
    public string Alice { get; }
    public string Bob { get; }

    public AutomationHost(
        WebApplicationFactory<Program> baseFactory,
        Dictionary<string, string?>? configuration = null,
        Action<IServiceCollection>? configure = null)
    {
        var dbName = Guid.NewGuid().ToString();
        Factory = baseFactory.WithWebHostBuilder(builder =>
        {
            if (configuration is not null)
                builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(configuration));
            builder.ConfigureServices(services => services.AddLogging(logging => logging.AddProvider(Logs)));
            builder.ConfigureTestServices(services =>
            {
                var dbDescriptors = services.Where(d =>
                    d.ServiceType == typeof(AppDbContext) ||
                    d.ServiceType == typeof(DbContextOptions<AppDbContext>) ||
                    (d.ServiceType.IsGenericType && d.ServiceType.GetGenericArguments().Contains(typeof(AppDbContext))))
                    .ToList();
                foreach (var d in dbDescriptors)
                    services.Remove(d);
                services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(dbName));
                services.AddTestAuthentication();
                services.Configure<AudioTranscription.Api.Configuration.UploadOptions>(o => o.TempStoragePath = Dir.Path);

                // Background services are driven by the tests themselves
                foreach (var hosted in services.Where(d => d.ServiceType == typeof(IHostedService) &&
                    (d.ImplementationType == typeof(TranscriptionWorker) ||
                     d.ImplementationType == typeof(WebhookDeliveryService) ||
                     d.ImplementationType == typeof(FolderWatchService))).ToList())
                    services.Remove(hosted);

                services.AddSingleton<TimeProvider>(Clock);
                services.AddSingleton(new WebhookHttpClient(new HttpClient(Receiver, disposeHandler: false)));
                configure?.Invoke(services);
            });
        });

        using var scope = Factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
        Alice = CreateUserAsync(users, AliceEmail).GetAwaiter().GetResult();
        Bob = CreateUserAsync(users, BobEmail).GetAwaiter().GetResult();
    }

    private static async Task<string> CreateUserAsync(UserManager<IdentityUser> users, string email)
    {
        var user = new IdentityUser { UserName = email, Email = email };
        (await users.CreateAsync(user, TestCredentials.NewPassword())).Succeeded.Should().BeTrue();
        return user.Id;
    }

    /// <summary>A client signed in (test scheme) as the given user.</summary>
    public HttpClient As(string userId)
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, userId);
        return client;
    }

    /// <summary>A client that presents the API key, as <c>Authorization: Bearer</c>, and no sign-in.</summary>
    public HttpClient WithKey(string key, bool asHeader = false)
    {
        var client = Factory.CreateClient();
        if (asHeader)
            client.DefaultRequestHeaders.Add("X-Api-Key", key);
        else
            client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", key);
        return client;
    }

    public async Task<T> WithDbAsync<T>(Func<AppDbContext, Task<T>> action)
    {
        using var scope = Factory.Services.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    public Task WithDbAsync(Func<AppDbContext, Task> action) => WithDbAsync<int>(async db =>
    {
        await action(db);
        return 0;
    });

    public async Task<Guid> AddJobAsync(string ownerId, string fileName = "meeting.mp3",
        AudioTranscription.Domain.Enums.AudioJobStatus status = AudioTranscription.Domain.Enums.AudioJobStatus.Completed)
    {
        var job = new AudioTranscription.Domain.Entities.AudioJob
        {
            FileName = fileName, OwnerId = ownerId, Status = status, Model = "Base", ContentType = "audio/mpeg",
            FileSizeBytes = 10, RawTranscript = "geheimer Inhalt", ErrorMessage = status == AudioTranscription.Domain.Enums.AudioJobStatus.Failed ? "interne Details" : null,
            Language = "de", DurationSeconds = 12.5, CompletedAtUtc = DateTime.UtcNow,
        };
        await WithDbAsync(async db =>
        {
            db.AudioJobs.Add(job);
            await db.SaveChangesAsync();
        });
        return job.Id;
    }

    public void Dispose() => Dir.Dispose();
}
