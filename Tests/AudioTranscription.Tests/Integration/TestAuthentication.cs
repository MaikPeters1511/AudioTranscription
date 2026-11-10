using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AudioTranscription.Tests.Integration;

/// <summary>
/// Authenticates every request as a test user, for integration tests that exercise protected
/// endpoints but are not about authentication itself. Without headers that is the fixed
/// <see cref="DefaultUserId"/>; tests about several users send <see cref="UserIdHeader"/> (and
/// optionally <see cref="RolesHeader"/>, comma-separated) to act as someone else.
/// </summary>
public class TestAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Test";
    public const string DefaultUserId = "test-user-id";
    public const string UserIdHeader = "X-Test-User";
    /// <summary>Value of <see cref="UserIdHeader"/> for a request that is authenticated but carries no user id claim.</summary>
    public const string NoUser = "none";
    public const string RolesHeader = "X-Test-Roles";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var userId = Request.Headers[UserIdHeader].FirstOrDefault() ?? DefaultUserId;
        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, "test@example.com"),
        };
        if (userId != NoUser)
            claims.Add(new Claim(ClaimTypes.NameIdentifier, userId));
        var roles = Request.Headers[RolesHeader].FirstOrDefault();
        if (!string.IsNullOrEmpty(roles))
            claims.AddRange(roles.Split(',').Select(role => new Claim(ClaimTypes.Role, role)));
        var identity = new ClaimsIdentity(claims, SchemeName);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
    }
}

public static class TestAuthenticationExtensions
{
    public static IServiceCollection AddTestAuthentication(this IServiceCollection services)
    {
        // Requests that carry an API key (S28) are authenticated by the real key scheme, all others by the test user
        const string TestOrApiKey = "TestOrApiKey";
        services.AddAuthentication(o =>
            {
                o.DefaultScheme = TestOrApiKey;
                o.DefaultAuthenticateScheme = TestOrApiKey;
                o.DefaultChallengeScheme = TestOrApiKey;
            })
            .AddPolicyScheme(TestOrApiKey, TestOrApiKey, o => o.ForwardDefaultSelector = context =>
                AudioTranscription.Api.Automation.ApiKeyAuthentication.IsApiKeyRequest(context.Request)
                    ? AudioTranscription.Api.Automation.ApiKeyAuthentication.Scheme
                    : TestAuthHandler.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });
        return services;
    }
}
