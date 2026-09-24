using AudioTranscription.Domain.Enums;

namespace AudioTranscription.Infrastructure.Services;

/// <summary>Builds the prompts for each post-processing mode (S10-T1). No I/O beyond reading embedded resources.</summary>
public interface IPostProcessingPromptCatalog
{
    /// <param name="targetLanguage">Required for <see cref="PostProcessingMode.Translate"/>, ignored otherwise.</param>
    PostProcessingPrompt ForFullTranscript(PostProcessingMode mode, string transcript, string? targetLanguage);

    /// <summary>Map step of chunked processing (Summary/ActionItems only).</summary>
    /// <param name="chunkIndex">0-based.</param>
    PostProcessingPrompt ForChunk(PostProcessingMode mode, string chunk, int chunkIndex, int chunkCount);

    /// <summary>Reduce step: combines the chunk results into one coherent result.</summary>
    PostProcessingPrompt ForReduce(PostProcessingMode mode, IReadOnlyList<string> partialResults);
}
