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

    // A single-reader channel cannot report its length, so the queue counts itself
    private int _count;

    /// <summary>Jobs waiting to be picked up by the worker (reported as a metric, S29).</summary>
    public int Count => Volatile.Read(ref _count);

    public async ValueTask EnqueueAsync(TranscriptionJobRequest request, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _count);
        try
        {
            await _channel.Writer.WriteAsync(request, cancellationToken);
        }
        catch
        {
            Interlocked.Decrement(ref _count);
            throw;
        }
    }

    /// <summary>
    /// Non-blocking read, used by tests to inspect the queue.
    /// </summary>
    internal bool TryRead([System.Diagnostics.CodeAnalysis.MaybeNullWhen(false)] out TranscriptionJobRequest request)
    {
        var read = _channel.Reader.TryRead(out request);
        if (read)
            Interlocked.Decrement(ref _count);
        return read;
    }

    public async IAsyncEnumerable<TranscriptionJobRequest> ReadAllAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var request in _channel.Reader.ReadAllAsync(cancellationToken))
        {
            Interlocked.Decrement(ref _count);
            yield return request;
        }
    }
}

public record TranscriptionJobRequest(Guid JobId, string FilePath);
