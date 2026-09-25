using AudioTranscription.Infrastructure.Services;
using Microsoft.Extensions.Options;
using Whisper.net.LibraryLoader;

namespace AudioTranscription.Api.BackgroundServices;

/// <summary>
/// Applies Whisper:RuntimeOrder (S15) to Whisper.net's native library loader and logs it.
/// Must start before any hosted service that could trigger the first model load (JobRecoveryService,
/// TranscriptionWorker), since Whisper.net.LibraryLoader.RuntimeOptions.RuntimeLibraryOrder only takes
/// effect if set before that first load. The runtime actually loaded is only known once that happens
/// (models load lazily per job), so it is logged separately in WhisperTranscriptionService.
/// </summary>
public class WhisperRuntimeConfigurator(IOptions<WhisperOptions> options, ILogger<WhisperRuntimeConfigurator> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (options.Value.RuntimeOrder.Length > 0)
        {
            RuntimeOptions.RuntimeLibraryOrder = options.Value.RuntimeOrder
                .Select(name =>
                {
                    WhisperOptions.TryParseRuntimeLibrary(name, out var library); // validated at startup (ValidateOnStart)
                    return library;
                })
                .ToList();
        }

        logger.LogInformation("Whisper native runtime order: [{Order}]", string.Join(", ", RuntimeOptions.RuntimeLibraryOrder));
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
