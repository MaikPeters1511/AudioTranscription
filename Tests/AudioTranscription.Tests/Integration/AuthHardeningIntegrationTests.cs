using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AudioTranscription.Api.Auth;
using AudioTranscription.Domain.Entities;
using AudioTranscription.Domain.Enums;
using AudioTranscription.Infrastructure.Data;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AudioTranscription.Tests.Integration;

/// <summary>S18-F1/F2: password length, lockout, rate limits for login and the user lookup by sharing, and the user list.</summary>
public class AuthHardeningIntegrationTests(WebApplicationFactory<Program> baseFactory) : IClassFixture<WebApplicationFactory<Program>>
{
    private const string AdminEmail = "admin@example.com";
    private const string UserEmail = "user@example.com";
    private const string OtherEmail = "other@example.com";
    private readonly string _adminPassword = TestCredentials.NewPassword();
    private readonly string _userPassword = TestCredentials.NewPassword();

    private WebApplicationFactory<Program> CreateFactory(params (string Key, string Value)[] settings)
    {
        var dbName = Guid.NewGuid().ToString();
        return baseFactory.WithWebHostBuilder(builder =>
        {
            foreach (var (key, value) in settings)
                builder.UseSetting(key, value);
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

    private async Task SeedAsync(WebApplicationFactory<Program> factory, params string[] extraEmails)
    {
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        if (!await roles.RoleExistsAsync(Roles.Admin))
            await roles.CreateAsync(new IdentityRole(Roles.Admin));

        var admin = new IdentityUser { UserName = AdminEmail, Email = AdminEmail };
        (await users.CreateAsync(admin, _adminPassword)).Succeeded.Should().BeTrue();
        await users.AddToRoleAsync(admin, Roles.Admin);
        (await users.CreateAsync(new IdentityUser { UserName = UserEmail, Email = UserEmail }, _userPassword)).Succeeded.Should().BeTrue();
        foreach (var email in extraEmails)
            (await users.CreateAsync(new IdentityUser { UserName = email, Email = email }, _userPassword)).Succeeded.Should().BeTrue();
    }

    private static HttpClient CreateClient(WebApplicationFactory<Program> factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });

    private static Task<HttpResponseMessage> LoginAttemptAsync(HttpClient client, string email, string password) =>
        client.PostAsJsonAsync("/api/auth/login?useCookies=true", new { email, password });

    private static async Task<HttpClient> LoginAsync(WebApplicationFactory<Program> factory, string email, string password)
    {
        var client = CreateClient(factory);
        (await LoginAttemptAsync(client, email, password)).StatusCode.Should().Be(HttpStatusCode.OK);
        return client;
    }

    // --- password length (S18-F2) -------------------------------------------------------

    [Fact]
    public async Task Admin_CannotCreateAUserWithAPasswordShorterThanTwelveCharacters()
    {
        using var factory = CreateFactory();
        await SeedAsync(factory);
        var admin = await LoginAsync(factory, AdminEmail, _adminPassword);

        var tooShort = await admin.PostAsJsonAsync("/api/admin/users", new { email = "kurz@example.com", password = "Abcdef1!xyz" }); // 11
        var longEnough = await admin.PostAsJsonAsync("/api/admin/users", new { email = "lang@example.com", password = "Abcdef1!xyzz" }); // 12

        tooShort.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await tooShort.Content.ReadAsStringAsync()).Should().Contain("12");
        longEnough.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task User_CannotChangeThePasswordToAShortOne()
    {
        using var factory = CreateFactory();
        await SeedAsync(factory);
        var user = await LoginAsync(factory, UserEmail, _userPassword);

        var tooShort = await user.PostAsJsonAsync("/api/auth/manage/info", new { oldPassword = _userPassword, newPassword = "Abcdef1!xyz" });
        var longEnough = await user.PostAsJsonAsync("/api/auth/manage/info", new { oldPassword = _userPassword, newPassword = TestCredentials.NewPassword() });

        tooShort.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        longEnough.IsSuccessStatusCode.Should().BeTrue();
    }

    // --- user list (S18-F2) -----------------------------------------------------------------

    private sealed record UserDto(string Id, string Email, string[] Roles, bool IsActive);
    private sealed record PageDto(List<UserDto> Items, int Total);

    [Fact]
    public async Task List_ShowsEachUsersRoles_InAStableOrder()
    {
        using var factory = CreateFactory();
        await SeedAsync(factory, "b@example.com", "a@example.com");
        var admin = await LoginAsync(factory, AdminEmail, _adminPassword);

        var page = await admin.GetFromJsonAsync<PageDto>("/api/admin/users?pageSize=100", new JsonSerializerOptions(JsonSerializerDefaults.Web));

        page!.Items.Select(u => u.Email).Should().Equal("a@example.com", "admin@example.com", "b@example.com", "user@example.com");
        page.Items.Single(u => u.Email == AdminEmail).Roles.Should().Equal(Roles.Admin);
        page.Items.Where(u => u.Email != AdminEmail).Should().OnlyContain(u => u.Roles.Length == 0);
    }

    [Fact]
    public async Task List_PagesWithoutRepeatingOrSkippingUsers()
    {
        using var factory = CreateFactory();
        await SeedAsync(factory, Enumerable.Range(0, 6).Select(i => $"user{i}@example.com").ToArray());
        var admin = await LoginAsync(factory, AdminEmail, _adminPassword);
        var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);

        var first = await admin.GetFromJsonAsync<PageDto>("/api/admin/users?page=1&pageSize=4", json);
        var second = await admin.GetFromJsonAsync<PageDto>("/api/admin/users?page=2&pageSize=4", json);

        first!.Items.Concat(second!.Items).Select(u => u.Id).Should().OnlyHaveUniqueItems().And.HaveCount(8);
        first.Total.Should().Be(8);
    }

    // --- lockout (S18-F1) ----------------------------------------------------------------------

    [Fact]
    public async Task Lockout_AfterTheConfiguredFailedAttempts_EvenTheRightPasswordIsRefused()
    {
        using var factory = CreateFactory(("Auth:Lockout:MaxFailedAttempts", "3"), ("Auth:LoginAttemptsPerMinute", "100"));
        await SeedAsync(factory);
        var client = CreateClient(factory);

        for (var i = 0; i < 3; i++)
            (await LoginAttemptAsync(client, UserEmail, "falsch-falsch-1!")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var rightPassword = await LoginAttemptAsync(client, UserEmail, _userPassword);
        rightPassword.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "the account is locked; the answer does not say so");
        (await rightPassword.Content.ReadAsStringAsync()).Should().NotContain("Locked");
    }

    [Fact]
    public async Task Lockout_OfOneAccount_DoesNotTouchAnother()
    {
        using var factory = CreateFactory(("Auth:Lockout:MaxFailedAttempts", "3"), ("Auth:LoginAttemptsPerMinute", "100"));
        await SeedAsync(factory);
        var client = CreateClient(factory);
        for (var i = 0; i < 3; i++)
            await LoginAttemptAsync(client, UserEmail, "falsch-falsch-1!");

        (await LoginAttemptAsync(CreateClient(factory), AdminEmail, _adminPassword)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Lockout_DefaultAllowsSomeTypos()
    {
        using var factory = CreateFactory(("Auth:LoginAttemptsPerMinute", "100"));
        await SeedAsync(factory);
        var client = CreateClient(factory);
        for (var i = 0; i < 5; i++)
            await LoginAttemptAsync(client, UserEmail, "falsch-falsch-1!");

        (await LoginAttemptAsync(client, UserEmail, _userPassword)).StatusCode.Should().Be(HttpStatusCode.OK, "five typos are not enough to lock the account by default");
    }

    // --- rate limit for login (S18-F1) -------------------------------------------------------------

    [Fact]
    public async Task Login_IsLimitedPerAddress_AndTheLimitAnswersWith429()
    {
        using var factory = CreateFactory(("Auth:LoginAttemptsPerMinute", "4"), ("Auth:Lockout:MaxFailedAttempts", "100"));
        await SeedAsync(factory);
        var client = CreateClient(factory);

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 6; i++)
            statuses.Add((await LoginAttemptAsync(client, $"gibt-es-nicht-{i}@example.com", "falsch-falsch-1!")).StatusCode);

        statuses.Take(4).Should().OnlyContain(s => s == HttpStatusCode.Unauthorized);
        statuses.Skip(4).Should().OnlyContain(s => s == HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task Login_NormalUseStaysUntouched()
    {
        using var factory = CreateFactory();
        await SeedAsync(factory);

        var admin = await LoginAsync(factory, AdminEmail, _adminPassword);
        var user = await LoginAsync(factory, UserEmail, _userPassword);

        (await admin.GetAsync("/api/auth/me")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await user.GetAsync("/api/auth/me")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // --- rate limit for the user lookup by sharing (S18-F1) -----------------------------------------

    private static async Task<Guid> SeedJobAsync(WebApplicationFactory<Program> factory, string email)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var owner = await db.Users.SingleAsync(u => u.Email == email);
        var job = new AudioJob { OwnerId = owner.Id, FileName = "a.mp3", ContentType = "audio/mpeg", Model = "Base", Status = AudioJobStatus.Completed };
        db.AudioJobs.Add(job);
        await db.SaveChangesAsync();
        return job.Id;
    }

    [Fact]
    public async Task ShareLookups_AreLimitedPerUser_SoAddressesCannotBeProbedInBulk()
    {
        using var factory = CreateFactory(("Auth:ShareRequestsPerMinute", "3"));
        await SeedAsync(factory, OtherEmail);
        var user = await LoginAsync(factory, UserEmail, _userPassword);
        var admin = await LoginAsync(factory, AdminEmail, _adminPassword);
        var userJob = await SeedJobAsync(factory, UserEmail);
        var adminJob = await SeedJobAsync(factory, AdminEmail);

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 5; i++)
            statuses.Add((await user.PostAsJsonAsync($"/api/audio-jobs/{userJob}/shares", new { email = $"unbekannt{i}@example.com" })).StatusCode);

        statuses.Take(3).Should().NotContain(HttpStatusCode.TooManyRequests);
        statuses.Skip(3).Should().OnlyContain(s => s == HttpStatusCode.TooManyRequests);
        (await admin.PostAsJsonAsync($"/api/audio-jobs/{adminJob}/shares", new { email = OtherEmail })).StatusCode
            .Should().NotBe(HttpStatusCode.TooManyRequests, "each user has their own limit");
    }
}
