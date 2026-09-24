namespace AudioTranscription.Api.Auth;

public class AuthOptions
{
    public const string SectionName = "Auth";

    /// <summary>Enables the self-service POST /api/auth/register endpoint. Off by default.</summary>
    public bool AllowRegistration { get; set; }

    /// <summary>
    /// First account, created at startup when no user exists yet.
    /// Provide via user-secrets or environment variables (Auth__InitialUser__Email/Password), never in appsettings.
    /// </summary>
    public InitialUserOptions InitialUser { get; set; } = new();

    public class InitialUserOptions
    {
        public string? Email { get; set; }
        public string? Password { get; set; }
    }
}
