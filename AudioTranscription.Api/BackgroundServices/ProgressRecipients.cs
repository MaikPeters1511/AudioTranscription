namespace AudioTranscription.Api.BackgroundServices;

/// <summary>
/// The users who get the progress of a running job. They are looked up again at most every <c>interval</c>, so a user whose
/// share was revoked while the job runs stops receiving progress (S19-F1); between lookups the last list is used, and
/// when a lookup fails the last known list stays. Several callers at once share one lookup.
/// </summary>
public sealed class ProgressRecipients(
    Func<CancellationToken, Task<IReadOnlyList<string>>> lookup,
    TimeProvider timeProvider,
    TimeSpan interval,
    IReadOnlyList<string> initial)
{
    private readonly Lock _lock = new();
    private IReadOnlyList<string> _recipients = initial;
    private long _lastLookupAt = timeProvider.GetTimestamp();
    private Task<IReadOnlyList<string>>? _running;

    public Task<IReadOnlyList<string>> GetAsync()
    {
        lock (_lock)
        {
            if (_running is not null)
                return _running;
            if (timeProvider.GetElapsedTime(_lastLookupAt) < interval)
                return Task.FromResult(_recipients);

            _running = RefreshAsync();
            return _running;
        }
    }

    private async Task<IReadOnlyList<string>> RefreshAsync()
    {
        // Returns to the caller first, so that _running is set before this method can finish and clear it
        await Task.Yield();
        try
        {
            var fresh = await lookup(CancellationToken.None);
            lock (_lock)
                _recipients = fresh;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Keep sending to the last known recipients; the next interval tries again
        }
        finally
        {
            lock (_lock)
            {
                _lastLookupAt = timeProvider.GetTimestamp();
                _running = null;
            }
        }

        lock (_lock)
            return _recipients;
    }
}
