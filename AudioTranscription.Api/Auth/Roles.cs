namespace AudioTranscription.Api.Auth;

/// <summary>
/// Role names used for RBAC (Role-Based Access Control). <see cref="Admin"/> unlocks the user
/// administration under /api/admin/users (S18); every other signed-in user has no role and sees
/// the same jobs as before (per-user jobs follow in S19).
/// </summary>
public static class Roles
{
    public const string Admin = "Admin";

    /// <summary>Authorization policy that requires <see cref="Admin"/>.</summary>
    public const string AdminPolicy = "AdminOnly";

    /// <summary>Roles an administrator may assign.</summary>
    public static readonly IReadOnlyList<string> Assignable = [Admin];
}
