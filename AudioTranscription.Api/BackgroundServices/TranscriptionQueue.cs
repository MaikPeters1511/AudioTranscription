using System.Threading.Channels;

namespace AudioTranscription.Api.BackgroundServices;

/// <summary>
/// Channel-based in-process queue for transcription jobs.
/// No external message broker needed for local operation.
/// </summary>
public class TranscriptionQueue
{
    private readonly Channel<TranscriptionJobRequest> _channel =
        Channel.CreateUnbounded<TranscriptionJobRequest>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });

    public async ValueTask EnqueueAsync(TranscriptionJobRequest request, CancellationToken cancellationToken = default)
    {
        await _channel.Writer.WriteAsync(request, cancellationToken);
    }

    /// <summary>
    /// Non-blocking read, used by tests to inspect the queue.
    /// </summary>
    internal bool TryRead([System.Diagnostics.CodeAnalysis.MaybeNullWhen(false)] out TranscriptionJobRequest request) =>
        _channel.Reader.TryRead(out request);

    public IAsyncEnumerable<TranscriptionJobRequest> ReadAllAsync(CancellationToken cancellationToken = default)
    {
        return _channel.Reader.ReadAllAsync(cancellationToken);
    }
}

public record TranscriptionJobRequest(Guid JobId, string FilePath);
