using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.Data;
using Microsoft.Extensions.Options;

namespace AudioTranscription.Api.Auth;

public record MeResponse(string Email, string[] Roles);

public static class AuthEndpoints
{
    public const string LoginRateLimitPolicy = "login";

    /// <summary>
    /// Identity endpoints that must work without a session. Everything else, including /manage/*,
    /// stays behind the fallback policy that requires a signed-in user.
    /// </summary>
    private static readonly string[] AnonymousIdentityEndpoints =
        ["/login", "/register", "/refresh", "/confirmEmail", "/resendConfirmationEmail", "/forgotPassword", "/resetPassword"];

    public static void MapAuthEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/auth").WithTags("Auth");

        // login (use ?useCookies=true), register, refresh, manage/*, ... from ASP.NET Core Identity
        var identityEndpoints = group.MapIdentityApi<IdentityUser>();
        identityEndpoints.Add(endpoint =>
        {
            var route = (endpoint as RouteEndpointBuilder)?.RoutePattern.RawText ?? string.Empty;
            if (AnonymousIdentityEndpoints.Any(path => route.EndsWith(path, StringComparison.OrdinalIgnoreCase)))
                endpoint.Metadata.Add(new AllowAnonymousAttribute());
            if (route.EndsWith("/login", StringComparison.OrdinalIgnoreCase))
                endpoint.Metadata.Add(new Microsoft.AspNetCore.RateLimiting.EnableRateLimitingAttribute(LoginRateLimitPolicy));
        });
        identityEndpoints
            .AddEndpointFilter(async (context, next) =>
            {
                var path = context.HttpContext.Request.Path.Value ?? string.Empty;
                var options = context.HttpContext.RequestServices.GetRequiredService<IOptions<AuthOptions>>().Value;
                var query = context.HttpContext.Request.Query;

                if (path.EndsWith("/register", StringComparison.OrdinalIgnoreCase) && !options.AllowRegistration)
                    return Results.NotFound();

                // Only cookie sessions are supported: bearer tokens would stay valid for up to an hour after
                // a user was deactivated or lost a role, because they are not re-checked on every request
                if (path.EndsWith("/refresh", StringComparison.OrdinalIgnoreCase))
                    return Results.NotFound();
                var isLogin = path.EndsWith("/login", StringComparison.OrdinalIgnoreCase);
                if (isLogin && !string.Equals(query["useCookies"], "true", StringComparison.OrdinalIgnoreCase))
                    return Results.BadRequest();

                // /manage/info could otherwise change the login identity (the e-mail address)
                if (path.EndsWith("/manage/info", StringComparison.OrdinalIgnoreCase)
                    && context.Arguments.OfType<InfoRequest>().Any(r => r.NewEmail is not null))
                    return Results.ValidationProblem(new Dictionary<string, string[]> { ["newEmail"] = ["Changing the e-mail address is not supported."] });

                var result = await next(context);

                // Identity names the reason (Failed, LockedOut, NotAllowed, ...). Hide it so a login attempt
                // does not reveal whether an account exists or has been deactivated
                var inner = result is INestedHttpResult nested ? nested.Result : result;
                return isLogin && inner is IStatusCodeHttpResult { StatusCode: StatusCodes.Status401Unauthorized }
                    ? Results.Unauthorized()
                    : result;
            });

        group.MapGet("/me", (ClaimsPrincipal user) =>
                TypedResults.Ok(new MeResponse(
                    user.FindFirstValue(ClaimTypes.Email) ?? user.Identity?.Name ?? string.Empty,
                    user.FindAll(ClaimTypes.Role).Select(c => c.Value).ToArray())))
            .WithName("GetCurrentUser")
            .WithSummary("Get the signed-in user")
            .WithDescription("Returns the signed-in user and their roles");

        group.MapPost("/logout", async Task<NoContent> (SignInManager<IdentityUser> signInManager) =>
            {
                await signInManager.SignOutAsync();
                return TypedResults.NoContent();
            })
            .WithName("Logout")
            .WithSummary("End the session")
            .WithDescription("Ends the cookie session");
    }
}
