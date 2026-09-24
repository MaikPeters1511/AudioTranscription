namespace AudioTranscription.Tests;

/// <summary>
/// Random credentials for test users. Generated at runtime so no password-like literals end up
/// in the repository (secret scanners flag them, and tests must not depend on fixed values).
/// </summary>
public static class TestCredentials
{
    /// <summary>Satisfies the default ASP.NET Core Identity password policy.</summary>
    public static string NewPassword() => $"Pw-{Guid.NewGuid():N}-Aa1!";
}
