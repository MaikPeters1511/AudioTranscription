namespace AudioTranscription.Api.BackgroundServices;

/// <summary>
/// Forwards progress values only when they rose and at least <see cref="MinInterval"/> passed since the
/// last one sent; 100 is always forwarded (once). Reports synchronously on the caller's thread, unlike
/// <see cref="Progress{T}"/>, so the order of values is kept.
/// </summary>
public sealed class ThrottledProgress(TimeProvider timeProvider, Action<int> send) : IProgress<int>
{
    public static readonly TimeSpan MinInterval = TimeSpan.FromMilliseconds(250);

    private readonly Lock _lock = new();
    private int _lastSent = -1;
    private long? _lastSentAt;

    public void Report(int value)
    {
        lock (_lock)
        {
            if (value <= _lastSent)
                return;
            var intervalPassed = _lastSentAt is null || timeProvider.GetElapsedTime(_lastSentAt.Value) >= MinInterval;
            if (value < 100 && !intervalPassed)
                return;

            _lastSent = value;
            _lastSentAt = timeProvider.GetTimestamp();
        }
        send(value);
    }
}
