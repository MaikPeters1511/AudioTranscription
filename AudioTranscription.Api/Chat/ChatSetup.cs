using System.ClientModel;
using System.Threading.RateLimiting;
using AudioTranscription.Api.Access;
using AudioTranscription.Infrastructure.Embeddings;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using OpenAI;

namespace AudioTranscription.Api.Chat;

/// <summary>
/// Registers the chat (S24, ADR 0011). It needs semantic search (retrieval) and a language model: local Ollama by default,
/// OpenAI only when <c>Chat:Provider=OpenAI</c> and a key are set. Without them the endpoints answer 503.
/// </summary>
public static class ChatSetup
{
    public static void AddChat(this WebApplicationBuilder builder, bool semanticSearchAvailable)
    {
        builder.Services.Configure<ChatSettings>(builder.Configuration.GetSection(ChatSettings.SectionName));
        var settings = builder.Configuration.GetSection(ChatSettings.SectionName).Get<ChatSettings>() ?? new ChatSettings();

        // The rate limit protects the model (and the bill of an external one); one bucket per user
        builder.Services.AddRateLimiter(limiter =>
        {
            limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            limiter.AddPolicy(ChatEndpoints.RateLimitPolicy, http => RateLimitPartition.GetFixedWindowLimiter(
                JobAccessService.GetUserId(http.User) ?? http.Connection.RemoteIpAddress?.ToString() ?? "anonymous",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = Math.Max(1, settings.RequestsPerMinute),
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                }));
        });

        if (!semanticSearchAvailable)
            return;
        var client = CreateClient(builder.Configuration, settings);
        if (client is null)
            return;

        builder.Services.AddSingleton(new ChatModelInfo(settings.Provider, settings.Model));
        builder.Services.AddScoped(sp => new ChatService(
            sp.GetRequiredService<IEmbeddingService>(), sp.GetRequiredService<IVectorIndex>(), client,
            sp.GetRequiredService<IOptions<ChatSettings>>(), sp.GetRequiredService<ILogger<ChatService>>()));
    }

    private static IChatClient? CreateClient(IConfiguration configuration, ChatSettings settings)
    {
        if (string.Equals(settings.Provider, ChatSettings.OpenAiProvider, StringComparison.OrdinalIgnoreCase))
        {
            // Opt-in: question and transcript passages leave this machine only with an explicit key
            if (string.IsNullOrWhiteSpace(settings.ApiKey))
                return null;
            var clientOptions = new OpenAIClientOptions();
            if (!string.IsNullOrWhiteSpace(settings.Endpoint))
                clientOptions.Endpoint = new Uri(settings.Endpoint);
            return new OpenAIClient(new ApiKeyCredential(settings.ApiKey), clientOptions).GetChatClient(settings.Model).AsIChatClient();
        }

        var endpoint = settings.Endpoint ?? configuration.GetConnectionString("llama3.2");
        return string.IsNullOrWhiteSpace(endpoint) ? null : new OllamaChatClient(new Uri(endpoint), settings.Model);
    }
}
