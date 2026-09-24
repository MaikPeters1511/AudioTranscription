using System.Threading.Channels;

namespace AudioTranscription.Api.BackgroundServices;

/// <summary>Channel-based in-process queue for variant generation requests (S10), separate from the
/// transcription queue so a slow LLM call never blocks or is blocked by Whisper jobs.</summary>
public class VariantQueue
{
    private readonly Channel<VariantJobRequest> _channel =
        Channel.CreateUnbounded<VariantJobRequest>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });

    public async ValueTask EnqueueAsync(VariantJobRequest request, CancellationToken cancellationToken = default)
    {
        await _channel.Writer.WriteAsync(request, cancellationToken);
    }

    internal bool TryRead([System.Diagnostics.CodeAnalysis.MaybeNullWhen(false)] out VariantJobRequest request) =>
        _channel.Reader.TryRead(out request);

    public IAsyncEnumerable<VariantJobRequest> ReadAllAsync(CancellationToken cancellationToken = default)
    {
        return _channel.Reader.ReadAllAsync(cancellationToken);
    }
}

public record VariantJobRequest(Guid VariantId);
