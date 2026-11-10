using System.Security.Claims;
using System.Text.Encodings.Web;
using AudioTranscription.Domain.Automation;
using AudioTranscription.Infrastructure.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AudioTranscription.Api.Automation;

/// <summary>Configuration of API key handling (S28, ADR 0009), section <c>ApiKeys</c>.</summary>
public class ApiKeyOptions
{
    public const string SectionName = "ApiKeys";

    /// <summary>Requests per minute and key; requests over the limit get 429.</summary>
    public int RequestsPerMinute { get; set; } = 300;
}

/// <summary>Where a request carries an API key and which authentication scheme handles it.</summary>
public static class ApiKeyAuthentication
{
    public const string Scheme = "ApiKey";
    public const string HeaderName = "X-Api-Key";
    /// <summary>Claim with the id of the key that authenticated the request.</summary>
    public const string KeyIdClaim = "api_key_id";

    /// <summary>
    /// The key of the request: <c>X-Api-Key</c>, or <c>Authorization: Bearer at_…</c>. Other bearer tokens (ASP.NET Core
    /// Identity's) are not API keys. Never read from the query string, which would put it into logs and history.
    /// </summary>
    public static string? ReadKey(HttpRequest request)
    {
        if (request.Headers.TryGetValue(HeaderName, out var header) && header.Count > 0 && !string.IsNullOrWhiteSpace(header[0]))
            return header[0]!.Trim();

        var authorization = request.Headers.Authorization.ToString();
        const string Bearer = "Bearer ";
        if (authorization.StartsWith(Bearer, StringComparison.OrdinalIgnoreCase))
        {
            var token = authorization[Bearer.Length..].Trim();
            if (token.StartsWith("at_", StringComparison.Ordinal))
                return token;
        }
        return null;
    }

    public static bool IsApiKeyRequest(HttpRequest request) => ReadKey(request) is not null;
}

/// <summary>
/// Authenticates a request by API key: finds the row by the public prefix, compares the hash in constant time and checks
/// expiry and the owner's account. The key is never logged, and every failure looks the same from outside.
/// </summary>
public class ApiKeyAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    TimeProvider time)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    private static readonly TimeSpan LastUsedGranularity = TimeSpan.FromMinutes(1);

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var key = ApiKeyAuthentication.ReadKey(Request);
        if (key is null)
            return AuthenticateResult.NoResult();
        if (!ApiKeyToken.TryParsePrefix(key, out var prefix))
            return AuthenticateResult.Fail("Invalid API key.");

        var services = Context.RequestServices;
        var db = services.GetRequiredService<AppDbContext>();
        var stored = await db.ApiKeys.SingleOrDefaultAsync(k => k.Prefix == prefix, Context.RequestAborted);
        var now = time.GetUtcNow().UtcDateTime;
        if (stored is null || !ApiKeyToken.Verify(key, stored.KeyHash) || (stored.ExpiresAtUtc is { } expires && expires <= now))
            return AuthenticateResult.Fail("Invalid API key.");

        var user = await services.GetRequiredService<UserManager<IdentityUser>>().FindByIdAsync(stored.OwnerId);
        // A deactivated account (lockout until the end of time, see the user administration) ends its keys as well
        if (user is null || user.LockoutEnd == DateTimeOffset.MaxValue)
            return AuthenticateResult.Fail("Invalid API key.");

        if (stored.LastUsedAtUtc is null || now - stored.LastUsedAtUtc >= LastUsedGranularity)
        {
            stored.LastUsedAtUtc = now;
            await db.SaveChangesAsync(Context.RequestAborted);
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, stored.OwnerId),
            new(ClaimTypes.Name, user.UserName ?? stored.OwnerId),
            new(ApiKeyAuthentication.KeyIdClaim, stored.Id.ToString()),
        };
        claims.AddRange(stored.Scopes.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(s => new Claim(ApiKeyScopes.ClaimType, s)));
        // No role claims: a key never acts as an administrator
        var identity = new ClaimsIdentity(claims, Scheme.Name);
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        Response.Headers.WWWAuthenticate = "Bearer";
        return Task.CompletedTask;
    }
}

/// <summary>
/// Enforces what a key may do, in one place: only the job API, and per HTTP method the scope <c>jobs:read</c> or
/// <c>jobs:write</c>. Browser sessions are not affected.
/// </summary>
public class ApiKeyScopeMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (context.User.Identity is { IsAuthenticated: true, AuthenticationType: ApiKeyAuthentication.Scheme })
        {
            var scopes = context.User.FindAll(ApiKeyScopes.ClaimType).Select(c => c.Value).ToHashSet();
            var path = context.Request.Path.Value ?? string.Empty;
            if (!ApiKeyScopes.IsPathAllowed(path) || !scopes.Contains(ApiKeyScopes.RequiredFor(path, context.Request.Method)))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }
        }
        await next(context);
    }
}
