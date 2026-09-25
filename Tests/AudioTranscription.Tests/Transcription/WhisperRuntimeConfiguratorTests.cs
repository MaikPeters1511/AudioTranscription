using AudioTranscription.Api.BackgroundServices;
using AudioTranscription.Infrastructure.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Whisper.net.LibraryLoader;

namespace AudioTranscription.Tests.Transcription;

/// <summary>
/// S15-T2: applying Whisper:RuntimeOrder touches the global, static
/// Whisper.net.LibraryLoader.RuntimeOptions.RuntimeLibraryOrder, so every test restores it.
/// </summary>
public class WhisperRuntimeConfiguratorTests : IDisposable
{
    private readonly List<RuntimeLibrary> _originalOrder = [.. RuntimeOptions.RuntimeLibraryOrder];

    public void Dispose() => RuntimeOptions.RuntimeLibraryOrder = _originalOrder;

    private static WhisperRuntimeConfigurator Sut(WhisperOptions options) =>
        new(Options.Create(options), NullLogger<WhisperRuntimeConfigurator>.Instance);

    [Fact]
    public async Task StartAsync_WithConfiguredOrder_SetsWhisperNetRuntimeLibraryOrder()
    {
        var sut = Sut(new WhisperOptions { RuntimeOrder = ["Cuda", "CoreML", "Cpu"] });

        await sut.StartAsync(CancellationToken.None);

        RuntimeOptions.RuntimeLibraryOrder.Should().Equal(RuntimeLibrary.Cuda, RuntimeLibrary.CoreML, RuntimeLibrary.Cpu);
    }

    [Fact]
    public async Task StartAsync_WithoutConfiguredOrder_LeavesWhisperNetDefaultOrderUnchanged()
    {
        var defaultOrder = _originalOrder;
        var sut = Sut(new WhisperOptions { RuntimeOrder = [] });

        await sut.StartAsync(CancellationToken.None);

        RuntimeOptions.RuntimeLibraryOrder.Should().Equal(defaultOrder);
    }

    [Fact]
    public async Task StartAsync_WhisperNetDefaultOrder_EndsInCpuAsAFallback()
    {
        // The AC "ohne GPU startet die App unverändert auf der CPU" relies on this: even without any
        // Whisper:RuntimeOrder configuration, Whisper.net's own default order always tries Cpu last.
        var sut = Sut(new WhisperOptions { RuntimeOrder = [] });

        await sut.StartAsync(CancellationToken.None);

        RuntimeOptions.RuntimeLibraryOrder.Should().Contain(RuntimeLibrary.Cpu);
    }
}
