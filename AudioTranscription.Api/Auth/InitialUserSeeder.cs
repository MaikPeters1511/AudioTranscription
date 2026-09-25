using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AudioTranscription.Api.Auth;

/// <summary>
/// Creates the first account from configuration (Auth:InitialUser) when no user exists yet, and
/// grants it the <see cref="Roles.Admin"/> role. Self-registration is disabled by default, so this
/// is how an installation gets its first (administrative) login.
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
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();

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
        if (!result.Succeeded)
        {
            // Never log the password itself, only Identity's validation messages
            logger.LogError("Could not create initial user {Email}: {Errors}",
                email, string.Join(" ", result.Errors.Select(e => e.Description)));
            return;
        }

        // Idempotent: only creates the role if it does not already exist (e.g. from a previous run).
        if (!await roleManager.RoleExistsAsync(Roles.Admin))
            await roleManager.CreateAsync(new IdentityRole(Roles.Admin));

        var roleResult = await userManager.AddToRoleAsync(user, Roles.Admin);
        if (!roleResult.Succeeded)
        {
            logger.LogError("Could not grant the {Role} role to the initial user {Email}: {Errors}",
                Roles.Admin, email, string.Join(" ", roleResult.Errors.Select(e => e.Description)));
            return;
        }

        logger.LogInformation("Created initial user {Email} with the {Role} role", email, Roles.Admin);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
