using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace AudioTranscription.Api.Auth;

public record MeResponse(string Email);

public static class AuthEndpoints
{
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
        });
        identityEndpoints
            .AddEndpointFilter(async (context, next) =>
            {
                var isRegister = context.HttpContext.Request.Path.Value?.EndsWith("/register", StringComparison.OrdinalIgnoreCase) == true;
                var options = context.HttpContext.RequestServices.GetRequiredService<IOptions<AuthOptions>>().Value;
                return isRegister && !options.AllowRegistration ? Results.NotFound() : await next(context);
            });

        group.MapGet("/me", (ClaimsPrincipal user) =>
                TypedResults.Ok(new MeResponse(user.FindFirstValue(ClaimTypes.Email) ?? user.Identity?.Name ?? string.Empty)))
            .WithName("GetCurrentUser")
            .WithSummary("Get the signed-in user")
            .WithDescription("Returns the signed-in user");

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
