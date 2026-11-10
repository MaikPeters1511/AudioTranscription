using System.Diagnostics.Metrics;

namespace AudioTranscription.Tests.Telemetry;

/// <summary>
/// Records what the meters of one <see cref="IMeterFactory"/> publish. Meters created by a factory are scoped
/// to it, so tests running in parallel (each with their own factory) cannot see each other's measurements.
/// </summary>
public sealed class MetricsCollector : IDisposable
{
    public sealed record Measurement(string Instrument, double Value, IReadOnlyDictionary<string, object?> Tags);

    private readonly MeterListener _listener = new();
    private readonly List<Measurement> _measurements = [];

    public MetricsCollector(IMeterFactory scope)
    {
        _listener.InstrumentPublished = (instrument, listener) =>
        {
            if (ReferenceEquals(instrument.Meter.Scope, scope))
                listener.EnableMeasurementEvents(instrument);
        };
        _listener.SetMeasurementEventCallback<long>((i, v, t, _) => Add(i, v, t));
        _listener.SetMeasurementEventCallback<int>((i, v, t, _) => Add(i, v, t));
        _listener.SetMeasurementEventCallback<double>((i, v, t, _) => Add(i, v, t));
        _listener.Start();
    }

    private void Add(Instrument instrument, double value, ReadOnlySpan<KeyValuePair<string, object?>> tags)
    {
        var copy = tags.ToArray().ToDictionary(t => t.Key, t => t.Value);
        lock (_measurements)
            _measurements.Add(new Measurement(instrument.Name, value, copy));
    }

    /// <summary>Asks observable instruments (gauges) for their current value.</summary>
    public void Observe() => _listener.RecordObservableInstruments();

    public IReadOnlyList<Measurement> Of(string instrument)
    {
        lock (_measurements)
            return _measurements.Where(m => m.Instrument == instrument).ToList();
    }

    public IReadOnlyList<Measurement> All()
    {
        lock (_measurements)
            return _measurements.ToList();
    }

    public void Dispose() => _listener.Dispose();
}
