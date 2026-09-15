using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using WebSocketNotifications.Diagnostics;

namespace WebSocketNotifications.Performance;

internal sealed class MetricCollector : IDisposable
{
    private readonly ConcurrentDictionary<string, long> counters = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<Instrument, long> gauges = new();
    private readonly MeterListener listener = new();
    private int observedTagCount;

    public MetricCollector()
    {
        listener.InstrumentPublished = (instrument, currentListener) =>
        {
            if (instrument.Meter.Name == WebSocketNotificationMetrics.MeterName)
            {
                currentListener.EnableMeasurementEvents(instrument);
            }
        };
        listener.MeasurementsCompleted = (instrument, _) => gauges.TryRemove(instrument, out long _);
        listener.SetMeasurementEventCallback<long>(Record);
        listener.Start();
    }

    public int ObservedTagCount => Volatile.Read(ref observedTagCount);

    public long Counter(string name) => counters.GetValueOrDefault(name);

    public long Gauge(string name)
    {
        listener.RecordObservableInstruments();
        return gauges.Where(pair => pair.Key.Name == name).Sum(pair => pair.Value);
    }

    public void Dispose() => listener.Dispose();

    private void Record(
        Instrument instrument,
        long measurement,
        ReadOnlySpan<KeyValuePair<string, object?>> tags,
        object? state)
    {
        if (tags.Length > 0)
        {
            Interlocked.Add(ref observedTagCount, tags.Length);
        }

        if (instrument is ObservableGauge<long>)
        {
            gauges[instrument] = measurement;
            return;
        }

        counters.AddOrUpdate(instrument.Name, measurement, (_, current) => current + measurement);
    }
}
