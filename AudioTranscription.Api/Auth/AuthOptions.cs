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

    /// <summary>
    /// Lockout after failed sign-ins. A lockout can be provoked on purpose for someone else's account (also the only admin's),
    /// so the defaults are generous and the rate limit below does the real work against guessing.
    /// </summary>
    public LockoutSettings Lockout { get; set; } = new();

    /// <summary>Sign-in attempts per minute and remote address; more are answered with 429. Behind a reverse proxy every user shares the proxy's address.</summary>
    public int LoginAttemptsPerMinute { get; set; } = 60;

    /// <summary>Requests per minute and user to share a job; limits probing which e-mail addresses have an account.</summary>
    public int ShareRequestsPerMinute { get; set; } = 30;

    public class LockoutSettings
    {
        /// <summary>Wrong passwords in a row before the account is locked.</summary>
        public int MaxFailedAttempts { get; set; } = 10;
        public int DurationMinutes { get; set; } = 5;
    }

    public class InitialUserOptions
    {
        public string? Email { get; set; }
        public string? Password { get; set; }
    }
}
