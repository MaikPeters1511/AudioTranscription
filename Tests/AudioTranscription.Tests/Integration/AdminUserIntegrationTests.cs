using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AudioTranscription.Api.Auth;
using AudioTranscription.Infrastructure.Data;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AudioTranscription.Tests.Integration;

/// <summary>S18: user administration under /api/admin/users, with real cookie authentication.</summary>
public class AdminUserIntegrationTests(WebApplicationFactory<Program> baseFactory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    private const string AdminEmail = "admin@example.com";
    private const string UserEmail = "user@example.com";
    private readonly string _adminPassword = TestCredentials.NewPassword();
    private readonly string _userPassword = TestCredentials.NewPassword();

    private sealed record UserDto(string Id, string Email, string[] Roles, bool IsActive);
    private sealed record PageDto(List<UserDto> Items, int Total);

    private WebApplicationFactory<Program> CreateFactory()
    {
        var dbName = Guid.NewGuid().ToString();
        return baseFactory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            var dbDescriptors = services.Where(d =>
                d.ServiceType == typeof(AppDbContext) ||
                d.ServiceType == typeof(DbContextOptions<AppDbContext>) ||
                (d.ServiceType.IsGenericType && d.ServiceType.GetGenericArguments().Contains(typeof(AppDbContext))))
                .ToList();
            foreach (var d in dbDescriptors)
                services.Remove(d);
            services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(dbName));
        }));
    }

    private async Task SeedAsync(WebApplicationFactory<Program> factory)
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
    }

    private static HttpClient CreateClient(WebApplicationFactory<Program> factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });

    private static async Task<HttpClient> LoginAsync(WebApplicationFactory<Program> factory, string email, string password)
    {
        var client = CreateClient(factory);
        var response = await client.PostAsJsonAsync("/api/auth/login?useCookies=true", new { email, password });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return client;
    }

    private static async Task<UserDto> FindAsync(HttpClient admin, string email)
    {
        var page = await admin.GetFromJsonAsync<PageDto>("/api/admin/users?pageSize=100", new JsonSerializerOptions(JsonSerializerDefaults.Web));
        return page!.Items.Single(u => u.Email == email);
    }

    [Fact]
    public async Task AdminEndpoints_AreForbiddenForNonAdmins()
    {
        using var factory = CreateFactory();
        await SeedAsync(factory);
        var user = await LoginAsync(factory, UserEmail, _userPassword);

        (await user.GetAsync("/api/admin/users")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await user.PostAsJsonAsync("/api/admin/users", new { email = "x@example.com", password = TestCredentials.NewPassword() }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task AdminEndpoints_RequireAuthentication()
    {
        using var factory = CreateFactory();
        await SeedAsync(factory);

        (await CreateClient(factory).GetAsync("/api/admin/users")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Me_ExposesRoles()
    {
        using var factory = CreateFactory();
        await SeedAsync(factory);
        var admin = await LoginAsync(factory, AdminEmail, _adminPassword);
        var user = await LoginAsync(factory, UserEmail, _userPassword);

        (await admin.GetFromJsonAsync<MeResponse>("/api/auth/me"))!.Roles.Should().Contain(Roles.Admin);
        (await user.GetFromJsonAsync<MeResponse>("/api/auth/me"))!.Roles.Should().BeEmpty();
    }

    [Fact]
    public async Task List_ReturnsUsersWithRolesAndStatus_WithoutSecrets()
    {
        using var factory = CreateFactory();
        await SeedAsync(factory);
        var admin = await LoginAsync(factory, AdminEmail, _adminPassword);

        var raw = await admin.GetStringAsync("/api/admin/users");
        var page = await admin.GetFromJsonAsync<PageDto>("/api/admin/users", new JsonSerializerOptions(JsonSerializerDefaults.Web));

        page!.Total.Should().Be(2);
        page.Items.Single(u => u.Email == AdminEmail).Roles.Should().Contain(Roles.Admin);
        page.Items.Single(u => u.Email == UserEmail).IsActive.Should().BeTrue();
        raw.Should().NotContainEquivalentOf("passwordhash").And.NotContainEquivalentOf("securitystamp");
    }

    [Fact]
    public async Task Create_AddsUserWhoCanLogIn()
    {
        using var factory = CreateFactory();
        await SeedAsync(factory);
        var admin = await LoginAsync(factory, AdminEmail, _adminPassword);
        var password = TestCredentials.NewPassword();

        var response = await admin.PostAsJsonAsync("/api/admin/users", new { email = "new@example.com", password });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        await LoginAsync(factory, "new@example.com", password);
    }

    [Fact]
    public async Task Create_WithWeakPassword_Returns400WithoutLeakingInternals()
    {
        using var factory = CreateFactory();
        await SeedAsync(factory);
        var admin = await LoginAsync(factory, AdminEmail, _adminPassword);

        var response = await admin.PostAsJsonAsync("/api/admin/users", new { email = "weak@example.com", password = "abc" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Create_WithDuplicateEmail_Returns409()
    {
        using var factory = CreateFactory();
        await SeedAsync(factory);
        var admin = await LoginAsync(factory, AdminEmail, _adminPassword);

        var response = await admin.PostAsJsonAsync("/api/admin/users", new { email = UserEmail, password = TestCredentials.NewPassword() });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task SetRoles_PromotesUser_AndRejectsUnknownRoles()
    {
        using var factory = CreateFactory();
        await SeedAsync(factory);
        var admin = await LoginAsync(factory, AdminEmail, _adminPassword);
        var user = await FindAsync(admin, UserEmail);

        (await admin.PutAsJsonAsync($"/api/admin/users/{user.Id}/roles", new { roles = new[] { "Superuser" } }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);

        (await admin.PutAsJsonAsync($"/api/admin/users/{user.Id}/roles", new { roles = new[] { Roles.Admin } }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await FindAsync(admin, UserEmail)).Roles.Should().Contain(Roles.Admin);
    }

    [Fact]
    public async Task LastActiveAdmin_CannotBeDemotedOrDeactivated()
    {
        using var factory = CreateFactory();
        await SeedAsync(factory);
        var admin = await LoginAsync(factory, AdminEmail, _adminPassword);
        var self = await FindAsync(admin, AdminEmail);

        (await admin.PutAsJsonAsync($"/api/admin/users/{self.Id}/roles", new { roles = Array.Empty<string>() }))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await admin.PostAsync($"/api/admin/users/{self.Id}/deactivate", null))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Admin_CanBeDemoted_WhenAnotherActiveAdminExists()
    {
        using var factory = CreateFactory();
        await SeedAsync(factory);
        var admin = await LoginAsync(factory, AdminEmail, _adminPassword);
        var user = await FindAsync(admin, UserEmail);
        await admin.PutAsJsonAsync($"/api/admin/users/{user.Id}/roles", new { roles = new[] { Roles.Admin } });
        var self = await FindAsync(admin, AdminEmail);

        (await admin.PutAsJsonAsync($"/api/admin/users/{self.Id}/roles", new { roles = Array.Empty<string>() }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Deactivate_EndsExistingSessions_AndBlocksLogin_UntilReactivated()
    {
        using var factory = CreateFactory();
        await SeedAsync(factory);
        var admin = await LoginAsync(factory, AdminEmail, _adminPassword);
        var user = await LoginAsync(factory, UserEmail, _userPassword);
        var target = await FindAsync(admin, UserEmail);
        (await user.GetAsync("/api/auth/me")).StatusCode.Should().Be(HttpStatusCode.OK);

        (await admin.PostAsync($"/api/admin/users/{target.Id}/deactivate", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await user.GetAsync("/api/auth/me")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await CreateClient(factory).PostAsJsonAsync("/api/auth/login?useCookies=true", new { email = UserEmail, password = _userPassword }))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await FindAsync(admin, UserEmail)).IsActive.Should().BeFalse();

        (await admin.PostAsync($"/api/admin/users/{target.Id}/activate", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        await LoginAsync(factory, UserEmail, _userPassword);
    }

    [Fact]
    public async Task UnknownUser_Returns404()
    {
        using var factory = CreateFactory();
        await SeedAsync(factory);
        var admin = await LoginAsync(factory, AdminEmail, _adminPassword);

        (await admin.PostAsync("/api/admin/users/does-not-exist/deactivate", null)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ChangePassword_ViaIdentityManageEndpoint_RequiresOldPassword()
    {
        using var factory = CreateFactory();
        await SeedAsync(factory);
        var user = await LoginAsync(factory, UserEmail, _userPassword);
        var newPassword = TestCredentials.NewPassword();

        (await user.PostAsJsonAsync("/api/auth/manage/info", new { oldPassword = TestCredentials.NewPassword(), newPassword }))
            .IsSuccessStatusCode.Should().BeFalse();
        (await user.PostAsJsonAsync("/api/auth/manage/info", new { oldPassword = _userPassword, newPassword }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        await LoginAsync(factory, UserEmail, newPassword);
    }
}
