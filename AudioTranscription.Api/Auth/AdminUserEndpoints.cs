using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace AudioTranscription.Api.Auth;

public record AdminUserDto(string Id, string Email, string[] Roles, bool IsActive);

public record AdminUserPage(List<AdminUserDto> Items, int Total);

public record CreateUserRequest(string? Email, string? Password, string[]? Roles);

public record SetRolesRequest(string[]? Roles);

/// <summary>User administration for the <see cref="Roles.Admin"/> role (S18).</summary>
public static class AdminUserEndpoints
{
    private const int MaxPageSize = 100;

    /// <summary>
    /// Serializes every change that can affect the number of active admins, so the "last active admin"
    /// check and the change that follows it cannot interleave. Covers a single API instance; a scaled-out
    /// deployment would need a database-level lock instead.
    /// </summary>
    private static readonly SemaphoreSlim AdminChangeLock = new(1, 1);

    public static void MapAdminUserEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/admin/users")
            .WithTags("Admin")
            .RequireAuthorization(Roles.AdminPolicy);

        group.MapGet("/", ListAsync)
            .WithName("ListUsers")
            .WithSummary("List users")
            .WithDescription("Paginated list of users with roles and active status");
        group.MapPost("/", CreateAsync)
            .WithName("CreateUser")
            .WithSummary("Create a user")
            .WithDescription("Creates a confirmed local account with an initial password");
        group.MapPut("/{id}/roles", SetRolesAsync)
            .WithName("SetUserRoles")
            .WithSummary("Set a user's roles")
            .WithDescription("Replaces the assignable roles of a user; the last active admin cannot be demoted");
        group.MapPost("/{id}/deactivate", (string id, UserManager<IdentityUser> users) => SetActiveAsync(id, false, users))
            .WithName("DeactivateUser")
            .WithSummary("Deactivate a user")
            .WithDescription("Blocks login and ends existing sessions; the last active admin cannot be deactivated");
        group.MapPost("/{id}/activate", (string id, UserManager<IdentityUser> users) => SetActiveAsync(id, true, users))
            .WithName("ActivateUser")
            .WithSummary("Reactivate a user")
            .WithDescription("Allows the user to log in again");
    }

    /// <summary>
    /// An account is deactivated only by an admin (lockout until the end of time). A temporary lockout from
    /// failed sign-ins is not a deactivation, so it must not make an admin look inactive.
    /// </summary>
    private static bool IsActive(IdentityUser user) => user.LockoutEnd != DateTimeOffset.MaxValue;

    private static async Task<AdminUserDto> ToDtoAsync(IdentityUser user, UserManager<IdentityUser> users) =>
        new(user.Id, user.Email ?? user.UserName ?? string.Empty, (await users.GetRolesAsync(user)).ToArray(), IsActive(user));

    private static async Task<Ok<AdminUserPage>> ListAsync(UserManager<IdentityUser> users, int page = 1, int pageSize = 25)
    {
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);
        page = Math.Max(page, 1);

        var total = await users.Users.CountAsync();
        var slice = await users.Users.OrderBy(u => u.Email).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
        var items = new List<AdminUserDto>(slice.Count);
        foreach (var user in slice)
            items.Add(await ToDtoAsync(user, users));
        return TypedResults.Ok(new AdminUserPage(items, total));
    }

    private static async Task<Results<Created<AdminUserDto>, ValidationProblem, Conflict<string>, ProblemHttpResult>> CreateAsync(
        CreateUserRequest request, UserManager<IdentityUser> users, RoleManager<IdentityRole> roleManager)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(request.Email) || !request.Email.Contains('@'))
            errors["email"] = ["A valid e-mail address is required."];
        if (string.IsNullOrEmpty(request.Password))
            errors["password"] = ["A password is required."];
        var roles = request.Roles ?? [];
        if (roles.Any(r => !Roles.Assignable.Contains(r)))
            errors["roles"] = ["Unknown role."];
        if (errors.Count > 0)
            return TypedResults.ValidationProblem(errors);

        if (await users.FindByEmailAsync(request.Email!) is not null)
            return TypedResults.Conflict("A user with this e-mail address already exists.");

        var user = new IdentityUser { UserName = request.Email, Email = request.Email, EmailConfirmed = true };
        var create = await users.CreateAsync(user, request.Password!);
        if (!create.Succeeded)
            return TypedResults.ValidationProblem(ToErrors(create, "password"));

        foreach (var role in roles.Distinct())
        {
            if (!await EnsureRoleAsync(roleManager, role) || !(await users.AddToRoleAsync(user, role)).Succeeded)
            {
                // Do not leave a half-created account behind
                await users.DeleteAsync(user);
                return TypedResults.Problem(statusCode: StatusCodes.Status500InternalServerError);
            }
        }

        return TypedResults.Created($"/api/admin/users/{user.Id}", await ToDtoAsync(user, users));
    }

    private static async Task<Results<NoContent, NotFound, ValidationProblem, Conflict<string>, ProblemHttpResult>> SetRolesAsync(
        string id, SetRolesRequest request, UserManager<IdentityUser> users, RoleManager<IdentityRole> roleManager)
    {
        var wanted = (request.Roles ?? []).Distinct().ToArray();
        if (wanted.Any(r => !Roles.Assignable.Contains(r)))
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["roles"] = ["Unknown role."] });

        await AdminChangeLock.WaitAsync();
        try
        {
            var user = await users.FindByIdAsync(id);
            if (user is null)
                return TypedResults.NotFound();

            var current = await users.GetRolesAsync(user);
            if (current.Contains(Roles.Admin) && !wanted.Contains(Roles.Admin) && IsActive(user) && await CountOtherActiveAdminsAsync(user, users) == 0)
                return TypedResults.Conflict("The last active admin cannot be demoted.");

            var remove = await users.RemoveFromRolesAsync(user, current.Except(wanted));
            if (!remove.Succeeded)
                return TypedResults.Problem(statusCode: StatusCodes.Status500InternalServerError);

            foreach (var role in wanted.Except(current))
            {
                if (!await EnsureRoleAsync(roleManager, role) || !(await users.AddToRoleAsync(user, role)).Succeeded)
                    return TypedResults.Problem(statusCode: StatusCodes.Status500InternalServerError);
            }

            // Role claims live in the cookie, so existing sessions must be refreshed
            await users.UpdateSecurityStampAsync(user);
            return TypedResults.NoContent();
        }
        finally
        {
            AdminChangeLock.Release();
        }
    }

    private static async Task<Results<NoContent, NotFound, Conflict<string>, ProblemHttpResult>> SetActiveAsync(
        string id, bool active, UserManager<IdentityUser> users)
    {
        await AdminChangeLock.WaitAsync();
        try
        {
            var user = await users.FindByIdAsync(id);
            if (user is null)
                return TypedResults.NotFound();

            if (!active)
            {
                if (await users.IsInRoleAsync(user, Roles.Admin) && IsActive(user) && await CountOtherActiveAdminsAsync(user, users) == 0)
                    return TypedResults.Conflict("The last active admin cannot be deactivated.");

                var ok = (await users.SetLockoutEnabledAsync(user, true)).Succeeded
                    && (await users.SetLockoutEndDateAsync(user, DateTimeOffset.MaxValue)).Succeeded
                    && (await users.UpdateSecurityStampAsync(user)).Succeeded; // ends existing sessions
                return ok ? TypedResults.NoContent() : TypedResults.Problem(statusCode: StatusCodes.Status500InternalServerError);
            }

            var reactivated = (await users.SetLockoutEndDateAsync(user, null)).Succeeded
                && (await users.ResetAccessFailedCountAsync(user)).Succeeded;
            return reactivated ? TypedResults.NoContent() : TypedResults.Problem(statusCode: StatusCodes.Status500InternalServerError);
        }
        finally
        {
            AdminChangeLock.Release();
        }
    }

    private static async Task<bool> EnsureRoleAsync(RoleManager<IdentityRole> roleManager, string role) =>
        await roleManager.RoleExistsAsync(role) || (await roleManager.CreateAsync(new IdentityRole(role))).Succeeded;

    private static async Task<int> CountOtherActiveAdminsAsync(IdentityUser user, UserManager<IdentityUser> users) =>
        (await users.GetUsersInRoleAsync(Roles.Admin)).Count(u => u.Id != user.Id && IsActive(u));

    private static Dictionary<string, string[]> ToErrors(IdentityResult result, string field) =>
        new() { [field] = result.Errors.Select(e => e.Description).ToArray() };
}
