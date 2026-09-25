using System.Collections.Concurrent;

namespace AudioTranscription.Infrastructure.Services;

/// <summary>
/// Loads a value once per key on first use and shares it between callers (e.g. one Whisper model per model type).
/// Failed loads are not cached. A caller that cancels stops waiting but does not abort the shared load;
/// loads are only aborted when the cache is disposed, which also disposes all loaded values.
/// </summary>
public sealed class KeyedAsyncCache<TKey, TValue>(Func<TKey, CancellationToken, Task<TValue>> load) : IAsyncDisposable
    where TKey : notnull
    where TValue : IDisposable
{
    private readonly ConcurrentDictionary<TKey, Lazy<Task<TValue>>> _entries = new();
    private readonly CancellationTokenSource _disposing = new();

    public async Task<TValue> GetAsync(TKey key, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposing.IsCancellationRequested, this);

        var entry = _entries.GetOrAdd(key, k => new Lazy<Task<TValue>>(() => load(k, _disposing.Token)));
        try
        {
            return await entry.Value.WaitAsync(cancellationToken);
        }
        catch (Exception) when (entry.Value.IsFaulted || entry.Value.IsCanceled)
        {
            // Let the next caller try again; only remove this entry, not a newer one
            _entries.TryRemove(KeyValuePair.Create(key, entry));
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposing.IsCancellationRequested)
            return;

        await _disposing.CancelAsync();
        foreach (var entry in _entries.Values.Where(e => e.IsValueCreated))
        {
            await ((Task)entry.Value).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            if (entry.Value.IsCompletedSuccessfully)
                entry.Value.Result.Dispose();
        }
        _entries.Clear();
        _disposing.Dispose();
    }
}
