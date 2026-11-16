using System.ClientModel;
using OpenAI;

namespace AudioTranscription.Api.Configuration;

/// <summary>
/// Connection to the local LM Studio server (OpenAI-compatible API, by default <c>http://localhost:1234/v1</c>) that serves
/// the chat model, the post-processing model and the embedding model. Configured in the section "LmStudio"; without an
/// endpoint the local language-model features stay off.
/// </summary>
public static class LmStudioConnection
{
    public const string SectionName = "LmStudio";
    public const string DefaultChatModel = "llama-3.2-3b-instruct";
    public const string DefaultEmbeddingModel = "text-embedding-bge-m3";

    /// <summary>LM Studio does not check the key, but the OpenAI client insists on one.</summary>
    private const string PlaceholderApiKey = "lm-studio";

    public static string? GetEndpoint(IConfiguration configuration)
    {
        var endpoint = configuration[$"{SectionName}:Endpoint"];
        return string.IsNullOrWhiteSpace(endpoint) ? null : endpoint;
    }

    /// <summary>Model used for post-processing (variants, chapters); the chat has its own setting.</summary>
    public static string GetModel(IConfiguration configuration)
    {
        var model = configuration[$"{SectionName}:Model"];
        return string.IsNullOrWhiteSpace(model) ? DefaultChatModel : model;
    }

    public static OpenAIClient CreateClient(string endpoint, string? apiKey = null) =>
        new(new ApiKeyCredential(string.IsNullOrWhiteSpace(apiKey) ? PlaceholderApiKey : apiKey),
            new OpenAIClientOptions { Endpoint = new Uri(endpoint) });
}
