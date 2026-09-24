using System.Reflection;
using AudioTranscription.Domain.Enums;

namespace AudioTranscription.Infrastructure.Services;

/// <summary>
/// Builds prompts from embedded resource templates under Prompts/ (S10-T1), one file per mode plus
/// chunk/reduce variants for the modes that support Map-Reduce chunking (Summary, ActionItems).
/// Keeping prompt text out of the workflow code makes it reviewable and testable on its own.
/// </summary>
public class PostProcessingPromptCatalog : IPostProcessingPromptCatalog
{
    private static readonly Assembly ResourceAssembly = typeof(PostProcessingPromptCatalog).Assembly;

    private static readonly IReadOnlyDictionary<PostProcessingMode, string> ModeFiles = new Dictionary<PostProcessingMode, string>
    {
        [PostProcessingMode.Cleanup] = "cleanup",
        [PostProcessingMode.Summary] = "summary",
        [PostProcessingMode.BulletPoints] = "bulletpoints",
        [PostProcessingMode.ActionItems] = "actionitems",
        [PostProcessingMode.Translate] = "translate",
    };

    /// <summary>Modes long transcripts can be chunked for (S10-T4); others are always sent in one call.</summary>
    private static readonly HashSet<PostProcessingMode> ChunkableModes = [PostProcessingMode.Summary, PostProcessingMode.ActionItems];

    public PostProcessingPrompt ForFullTranscript(PostProcessingMode mode, string transcript, string? targetLanguage)
    {
        if (mode == PostProcessingMode.Translate && string.IsNullOrWhiteSpace(targetLanguage))
            throw new ArgumentException("Translate requires a target language.", nameof(targetLanguage));

        var systemMessage = LoadTemplate(ModeFiles[mode]);
        if (mode == PostProcessingMode.Translate)
            systemMessage = systemMessage.Replace("{{TARGET_LANGUAGE}}", targetLanguage);

        return new PostProcessingPrompt(systemMessage, $"Transcript:\n\n{transcript}");
    }

    public PostProcessingPrompt ForChunk(PostProcessingMode mode, string chunk, int chunkIndex, int chunkCount)
    {
        if (!ChunkableModes.Contains(mode))
            throw new NotSupportedException($"{mode} does not support chunking.");

        var systemMessage = LoadTemplate($"{ModeFiles[mode]}-chunk")
            .Replace("{{CHUNK_INDEX}}", (chunkIndex + 1).ToString())
            .Replace("{{CHUNK_COUNT}}", chunkCount.ToString());
        return new PostProcessingPrompt(systemMessage, $"Excerpt:\n\n{chunk}");
    }

    public PostProcessingPrompt ForReduce(PostProcessingMode mode, IReadOnlyList<string> partialResults)
    {
        if (!ChunkableModes.Contains(mode))
            throw new NotSupportedException($"{mode} does not support chunking.");

        var systemMessage = LoadTemplate($"{ModeFiles[mode]}-reduce");
        var userMessage = string.Join("\n\n---\n\n",
            partialResults.Select((partial, i) => $"Part {i + 1}:\n{partial}"));
        return new PostProcessingPrompt(systemMessage, userMessage);
    }

    private static string LoadTemplate(string name)
    {
        var resourceName = $"{ResourceAssembly.GetName().Name}.Prompts.{name}.txt";
        using var stream = ResourceAssembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Prompt template '{resourceName}' is missing.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd().Trim();
    }
}
