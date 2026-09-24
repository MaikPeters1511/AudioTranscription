using AudioTranscription.Domain.Enums;

namespace AudioTranscription.Infrastructure.Services;

/// <summary>Generates an on-demand transcript variant (S10) via a local LLM.</summary>
public interface IVariantGenerator
{
    /// <exception cref="Exception">The variant could not be generated; the caller marks it Failed.</exception>
    Task<string> GenerateAsync(PostProcessingMode mode, string transcript, string? targetLanguage, CancellationToken cancellationToken = default);
}
