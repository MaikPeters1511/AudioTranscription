using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AudioTranscription.Api.Auth;

/// <summary>
/// Creates the first account from configuration (Auth:InitialUser) when no user exists yet.
/// Self-registration is disabled by default, so this is how an installation gets its first login.
/// </summary>
public class InitialUserSeeder(
    IServiceScopeFactory scopeFactory,
    IOptions<AuthOptions> options,
    ILogger<InitialUserSeeder> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();

        if (await userManager.Users.AnyAsync(cancellationToken))
            return;

        var (email, password) = (options.Value.InitialUser.Email, options.Value.InitialUser.Password);
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            logger.LogWarning(
                "No user exists and Auth:InitialUser:Email/Password is not configured; nobody can log in. " +
                "Set them via user-secrets or environment variables (Auth__InitialUser__Email, Auth__InitialUser__Password)");
            return;
        }

        var user = new IdentityUser { UserName = email, Email = email, EmailConfirmed = true };
        var result = await userManager.CreateAsync(user, password);
        if (result.Succeeded)
        {
            logger.LogInformation("Created initial user {Email}", email);
            return;
        }

        // Never log the password itself, only Identity's validation messages
        logger.LogError("Could not create initial user {Email}: {Errors}",
            email, string.Join(" ", result.Errors.Select(e => e.Description)));
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
