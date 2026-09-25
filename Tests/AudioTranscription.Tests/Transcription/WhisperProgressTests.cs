using AudioTranscription.Infrastructure.Services;
using FluentAssertions;

namespace AudioTranscription.Tests.Transcription;

public class WhisperProgressTests
{
    /// <summary>Records synchronously; <see cref="Progress{T}"/> would post to the thread pool.</summary>
    private sealed class RecordingProgress : IProgress<int>
    {
        public List<int> Values { get; } = [];
        public void Report(int value) => Values.Add(value);
    }

    [Fact]
    public void Handler_ForwardsWhisperValuesToProgress()
    {
        var progress = new RecordingProgress();
        var handler = WhisperProgress.Handler(progress);

        foreach (var value in new[] { 0, 1, 37, 99, 100 })
            handler(value);

        progress.Values.Should().Equal(0, 1, 37, 99, 100);
    }

    [Fact]
    public void Handler_ClampsValuesOutsideZeroToHundred()
    {
        var progress = new RecordingProgress();
        var handler = WhisperProgress.Handler(progress);

        handler(-5);
        handler(140);

        progress.Values.Should().Equal(0, 100);
    }
}
