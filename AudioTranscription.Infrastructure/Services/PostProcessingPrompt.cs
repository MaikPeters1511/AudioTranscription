namespace AudioTranscription.Infrastructure.Services;

/// <summary>A ready-to-send prompt for the chat model.</summary>
public record PostProcessingPrompt(string SystemMessage, string UserMessage);
