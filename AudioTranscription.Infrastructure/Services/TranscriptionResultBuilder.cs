using System.Text;

namespace AudioTranscription.Infrastructure.Services;

/// <summary>
/// Collects the segments Whisper reports and turns them into a <see cref="TranscriptionResult"/>.
/// Kept apart from <see cref="WhisperTranscriptionService"/> so it can be tested without a model.
/// </summary>
public class TranscriptionResultBuilder
{
    private readonly StringBuilder _text = new();
    private readonly List<SegmentResult> _segments = [];
    private string? _detectedLanguage;
    private TimeSpan _end;

    public void Add(TimeSpan start, TimeSpan end, string text, string? language)
    {
        _text.Append(text);
        _segments.Add(new SegmentResult(start, end, text.Trim()));
        _detectedLanguage ??= language;
        if (end > _end)
            _end = end;
    }

    /// <param name="requestedLanguage">Language set at upload; used if Whisper reports none.</param>
    public TranscriptionResult Build(string? requestedLanguage) =>
        new(_text.ToString().Trim(), _detectedLanguage ?? requestedLanguage, _end > TimeSpan.Zero ? _end.TotalSeconds : null)
        {
            Segments = _segments.ToList()
        };
}
