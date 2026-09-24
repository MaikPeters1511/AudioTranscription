using System.Net;
using System.Net.Http.Json;
using AudioTranscription.Infrastructure.Data;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AudioTranscription.Tests.Integration;

/// <summary>Real cookie authentication against the API (no test auth handler).</summary>
public class AuthIntegrationTests(WebApplicationFactory<Program> baseFactory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    private const string Email = "alice@example.com";
    private readonly string _password = TestCredentials.NewPassword();
    private const string AllowedOrigin = "https://transcription.example.com";

    private WebApplicationFactory<Program> CreateFactory(bool allowRegistration = false)
    {
        var dbName = Guid.NewGuid().ToString();
        return baseFactory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Auth:AllowRegistration"] = allowRegistration.ToString(),
                ["Cors:AllowedOrigins:0"] = AllowedOrigin,
            }));
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
            });
        });
    }

    private async Task SeedUserAsync(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
        var result = await users.CreateAsync(new IdentityUser { UserName = Email, Email = Email }, _password);
        result.Succeeded.Should().BeTrue(string.Join(", ", result.Errors.Select(e => e.Description)));
    }

    // Secure cookies are only sent over https, so the test client uses an https base address
    private static HttpClient CreateClient(WebApplicationFactory<Program> factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });

    private Task<HttpResponseMessage> LoginAsync(HttpClient client, string? password = null) =>
        client.PostAsJsonAsync("/api/auth/login?useCookies=true", new { email = Email, password = password ?? _password });

    [Fact]
    public async Task Login_WithValidCredentials_SetsHardenedSessionCookie()
    {
        using var factory = CreateFactory();
        await SeedUserAsync(factory);

        var response = await LoginAsync(CreateClient(factory));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var cookie = response.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith(".AspNetCore.Identity.Application="));
        cookie.Should().ContainEquivalentOf("httponly")
            .And.ContainEquivalentOf("secure")
            .And.ContainEquivalentOf("samesite=strict");
    }

    [Fact]
    public async Task Login_WithWrongPassword_Returns401()
    {
        using var factory = CreateFactory();
        await SeedUserAsync(factory);

        var response = await LoginAsync(CreateClient(factory), TestCredentials.NewPassword());

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Me_ReturnsCurrentUserOnlyWhenLoggedIn()
    {
        using var factory = CreateFactory();
        await SeedUserAsync(factory);
        var client = CreateClient(factory);

        (await client.GetAsync("/api/auth/me")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        await LoginAsync(client);
        var me = await client.GetFromJsonAsync<MeResponse>("/api/auth/me");
        me!.Email.Should().Be(Email);
    }

    [Fact]
    public async Task Logout_EndsTheSession()
    {
        using var factory = CreateFactory();
        await SeedUserAsync(factory);
        var client = CreateClient(factory);
        await LoginAsync(client);

        var logout = await client.PostAsync("/api/auth/logout", null);

        logout.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await client.GetAsync("/api/auth/me")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Register_IsDisabledByDefault()
    {
        using var factory = CreateFactory();

        var response = await CreateClient(factory).PostAsJsonAsync("/api/auth/register", new { email = "mallory@example.com", password = _password });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        using var scope = factory.Services.CreateScope();
        (await scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>().Users.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Register_CanBeEnabledByConfiguration()
    {
        using var factory = CreateFactory(allowRegistration: true);

        var response = await CreateClient(factory).PostAsJsonAsync("/api/auth/register", new { email = "bob@example.com", password = _password });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData("GET", "/api/audio-jobs")]
    [InlineData("GET", "/api/audio-jobs/00000000-0000-0000-0000-000000000001")]
    [InlineData("POST", "/api/audio-jobs")]
    [InlineData("POST", "/hubs/transcription/negotiate?negotiateVersion=1")]
    [InlineData("GET", "/api/auth/manage/info")]
    [InlineData("GET", "/api/transcription-options")]
    [InlineData("GET", "/api/audio-jobs/00000000-0000-0000-0000-000000000001/segments")]
    [InlineData("GET", "/api/audio-jobs/00000000-0000-0000-0000-000000000001/subtitles?format=srt")]
    [InlineData("GET", "/api/audio-jobs/00000000-0000-0000-0000-000000000001/audio")]
    public async Task ProtectedEndpoints_WithoutLogin_Return401(string method, string url)
    {
        using var factory = CreateFactory();

        var response = await CreateClient(factory).SendAsync(new HttpRequestMessage(new HttpMethod(method), url));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ProtectedEndpoints_AfterLogin_AreAccessible()
    {
        using var factory = CreateFactory();
        await SeedUserAsync(factory);
        var client = CreateClient(factory);
        await LoginAsync(client);

        (await client.GetAsync("/api/audio-jobs")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.PostAsync("/hubs/transcription/negotiate?negotiateVersion=1", null)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData(AllowedOrigin, true)]
    [InlineData("https://evil.example.org", false)]
    public async Task Cors_OnlyAllowsConfiguredOrigins(string origin, bool allowed)
    {
        using var factory = CreateFactory();
        var request = new HttpRequestMessage(HttpMethod.Options, "/api/audio-jobs");
        request.Headers.Add("Origin", origin);
        request.Headers.Add("Access-Control-Request-Method", "GET");

        var response = await CreateClient(factory).SendAsync(request);

        response.Headers.TryGetValues("Access-Control-Allow-Origin", out var values).Should().Be(allowed);
        if (allowed)
            values.Should().ContainSingle().Which.Should().Be(origin);
    }

    private record MeResponse(string Email);
}
