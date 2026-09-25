namespace AudioTranscription.Api.Auth;

/// <summary>
/// Role names used for RBAC (Role-Based Access Control). Currently only used to mark the
/// initial account seeded by <see cref="InitialUserSeeder"/> as an administrator; endpoints
/// do not yet require it (S17 keeps all jobs shared between every signed-in user).
/// </summary>
public static class Roles
{
    public const string Admin = "Admin";
}
