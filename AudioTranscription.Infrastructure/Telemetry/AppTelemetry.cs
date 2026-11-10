using System.Diagnostics;

namespace AudioTranscription.Infrastructure.Telemetry;

/// <summary>
/// Name of the application's trace source and meter (S29). <c>ServiceDefaults</c> registers both with
/// OpenTelemetry by this literal name because it cannot reference this project; a test keeps them in sync.
/// </summary>
public static class AppTelemetry
{
    public const string Name = "AudioTranscription";

    /// <summary>
    /// Spans of the processing pipeline. Spans carry ids and bounded values only (model, mode, outcome),
    /// never file names, transcript text or exception messages: those can contain personal data.
    /// </summary>
    public static readonly ActivitySource Source = new(Name);
}
