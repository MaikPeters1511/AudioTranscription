using AudioTranscription.Domain.Diarization;

namespace AudioTranscription.Infrastructure.Services;

/// <summary>Assigns time ranges of an audio file to anonymous speakers (S11), fully offline.</summary>
public interface IDiarizationService
{
    /// <param name="audioFilePath">Any audio format understood by ffmpeg; converted internally.</param>
    /// <param name="expectedSpeakerCount">A known number of speakers, if the caller has one; null lets clustering decide via the configured threshold.</param>
    /// <returns>Speaker intervals in the audio's timeline. Empty if no speech/speakers were detected.</returns>
    Task<IReadOnlyList<SpeakerInterval>> DiarizeAsync(string audioFilePath, int? expectedSpeakerCount, CancellationToken cancellationToken = default);
}
