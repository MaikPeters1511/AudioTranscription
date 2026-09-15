using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace AudioTranscription.Infrastructure.Services;

/// <summary>
/// Uses a local Ollama LLM model to clean up and format the raw transcription text.
/// Requires the IChatClient to be injected (configured via Aspire).
/// </summary>
public class OllamaPostProcessor : ITranscriptPostProcessor
{
    private readonly IChatClient _chatClient;
    private readonly ILogger<OllamaPostProcessor> _logger;

    public OllamaPostProcessor(IChatClient chatClient, ILogger<OllamaPostProcessor> logger)
    {
        _chatClient = chatClient;
        _logger = logger;
    }

    public async Task<string> ProcessAsync(string rawTranscript, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(rawTranscript))
            return rawTranscript;

        _logger.LogInformation("Starting Ollama post-processing on {CharCount} characters", rawTranscript.Length);

        try
        {
            var prompt = $@"
Please clean up the following transcribed text.
Rules:
1. Fix obvious punctuation and capitalization errors.
2. Remove filler words (ums, uhs) if they add no value.
3. Add paragraph breaks where appropriate to make it readable.
4. Do NOT change the meaning or rewrite the sentences in your own words.
5. Only output the cleaned text, without any introductory or concluding remarks.

Raw transcript:
{rawTranscript}
";

            var response = await _chatClient.GetResponseAsync(prompt, cancellationToken: cancellationToken);
            var cleanedText = response.Text?.Trim();

            if (!string.IsNullOrWhiteSpace(cleanedText))
            {
                _logger.LogInformation("Ollama post-processing complete");
                return cleanedText;
            }

            _logger.LogWarning("Ollama returned empty text, falling back to raw transcript");
            return rawTranscript;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ollama post-processing failed, falling back to raw transcript");
            return rawTranscript;
        }
    }
}
