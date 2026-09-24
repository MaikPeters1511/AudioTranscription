using AudioTranscription.Api.Dtos;
using AudioTranscription.Infrastructure.Services;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;

namespace AudioTranscription.Api.Endpoints;

public static class TranscriptionOptionsEndpoints
{
    public static void MapTranscriptionOptionsEndpoints(this WebApplication app)
    {
        app.MapGet("/api/transcription-options", GetTranscriptionOptions)
            .WithName("GetTranscriptionOptions")
            .WithDescription("Models and languages that can be chosen for an upload; 'auto' (language detection) is always available");
    }

    private static Ok<TranscriptionOptionsDto> GetTranscriptionOptions(IOptions<WhisperOptions> whisperOptions)
    {
        var options = whisperOptions.Value;
        return TypedResults.Ok(new TranscriptionOptionsDto(options.AllowedModels, options.DefaultModel, options.SupportedLanguages));
    }
}
