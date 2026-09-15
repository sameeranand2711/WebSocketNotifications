using System.Diagnostics;

namespace WebSocketNotifications.Performance;

internal static class PerformanceRunner
{
    public static async Task<PerformanceResult> RunAsync(
        PerformanceOptions options,
        CancellationToken cancellationToken)
    {
        using var collector = new MetricCollector();
        using var process = Process.GetCurrentProcess();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var initialWorkingSet = process.WorkingSet64;
        var initialManagedMemory = GC.GetTotalMemory(forceFullCollection: false);
        var initialAllocated = GC.GetTotalAllocatedBytes(precise: true);
        var initialCpu = process.TotalProcessorTime;
        var peakWorkingSet = initialWorkingSet;
        var peakManagedMemory = initialManagedMemory;
        var peakQueueDepth = 0L;
        var peakCpuPercent = 0d;
        var cpuSamples = new List<double>();
        var servers = Enumerable.Range(0, options.ServerCount)
            .Select(index => SimulatedServer.Start(index, options))
            .ToList();

        var stopwatch = Stopwatch.StartNew();
        var nextPublish = TimeSpan.Zero;
        var nextSample = TimeSpan.FromSeconds(1);
        var nextChurn = TimeSpan.FromSeconds(options.ChurnIntervalSeconds);
        var interruptionAt = TimeSpan.FromTicks(TimeSpan.FromSeconds(options.DurationSeconds).Ticks / 3);
        var restartAt = TimeSpan.FromTicks(TimeSpan.FromSeconds(options.DurationSeconds).Ticks * 2 / 3);
        var interrupted = false;
        var restarted = false;
        var sourceMessages = 0L;
        var dispatches = 0L;
        // Exclude connection/subscription setup from the first timed CPU sample.
        var previousCpu = process.TotalProcessorTime;
        var previousSampleElapsed = TimeSpan.Zero;

        try
        {
            while (stopwatch.Elapsed < TimeSpan.FromSeconds(options.DurationSeconds))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var elapsed = stopwatch.Elapsed;

                if (!interrupted && elapsed >= interruptionAt)
                {
                    interrupted = true;
                    await Task.Delay(TimeSpan.FromSeconds(options.SourceInterruptionSeconds), cancellationToken)
                        .ConfigureAwait(false);
                    nextPublish = stopwatch.Elapsed;
                }

                if (!restarted && stopwatch.Elapsed >= restartAt)
                {
                    restarted = true;
                    await servers[^1].DisposeAsync().ConfigureAwait(false);
                    servers[^1] = SimulatedServer.Start(options.ServerCount - 1, options);
                }

                if (options.ChurnPercent > 0 && stopwatch.Elapsed >= nextChurn)
                {
                    foreach (var server in servers)
                    {
                        await server.ChurnAsync(cancellationToken).ConfigureAwait(false);
                    }

                    nextChurn += TimeSpan.FromSeconds(options.ChurnIntervalSeconds);
                }

                if (stopwatch.Elapsed >= nextPublish)
                {
                    var notification = SimulatedServer.CreateNotification(sourceMessages, options.PayloadBytes);
                    foreach (var server in servers)
                    {
                        await server.PublishAsync(notification, cancellationToken).ConfigureAwait(false);
                        dispatches++;
                    }

                    sourceMessages++;
                    nextPublish += TimeSpan.FromSeconds(1d / options.MessageRate);
                }

                if (stopwatch.Elapsed >= nextSample)
                {
                    process.Refresh();
                    peakWorkingSet = Math.Max(peakWorkingSet, process.WorkingSet64);
                    peakManagedMemory = Math.Max(peakManagedMemory, GC.GetTotalMemory(forceFullCollection: false));
                    peakQueueDepth = Math.Max(
                        peakQueueDepth,
                        collector.Gauge("websocket_notifications.messages.queued"));
                    var cpuNow = process.TotalProcessorTime;
                    var sampleDuration = stopwatch.Elapsed - previousSampleElapsed;
                    var cpuPercent = (cpuNow - previousCpu).TotalMilliseconds /
                        sampleDuration.TotalMilliseconds /
                        Environment.ProcessorCount * 100;
                    cpuSamples.Add(cpuPercent);
                    peakCpuPercent = Math.Max(peakCpuPercent, cpuPercent);
                    previousCpu = cpuNow;
                    previousSampleElapsed = stopwatch.Elapsed;
                    nextSample += TimeSpan.FromSeconds(1);
                }

                var delay = nextPublish - stopwatch.Elapsed;
                if (delay > TimeSpan.Zero)
                {
                    await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                }
            }
        }
        finally
        {
            foreach (var server in servers)
            {
                await server.StopAsync().ConfigureAwait(false);
            }
        }

        try
        {
            collector.Gauge("websocket_notifications.messages.queued");
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            process.Refresh();
            var finalWorkingSet = process.WorkingSet64;
            var finalManagedMemory = GC.GetTotalMemory(forceFullCollection: true);
            var expectedDispatches = sourceMessages * options.ServerCount;
            var notificationsReceived = collector.Counter("websocket_notifications.notifications.received");
            var notificationsMatched = collector.Counter("websocket_notifications.notifications.matched");
            var messagesEnqueued = collector.Counter("websocket_notifications.messages.enqueued");
            var messagesSent = collector.Counter("websocket_notifications.messages.sent");
            var messagesDropped = collector.Counter("websocket_notifications.messages.dropped");
            var expectedRecipientMatches = dispatches * options.ConnectionsPerServer;
            var shutdownDiscardedMessages = messagesEnqueued - messagesSent;
            var queuedAtEnd = collector.Gauge("websocket_notifications.messages.queued");
            var passed = dispatches == expectedDispatches &&
                notificationsReceived == expectedDispatches &&
                notificationsMatched == expectedDispatches &&
                expectedRecipientMatches == messagesEnqueued + messagesDropped &&
                shutdownDiscardedMessages >= 0 &&
                messagesSent > 0 &&
                collector.Counter("websocket_notifications.messages.send_failures") == 0 &&
                collector.Counter("websocket_notifications.notifications.unmatched") == 0 &&
                queuedAtEnd == 0 &&
                collector.Gauge("websocket_notifications.connections.active") == 0 &&
                collector.Gauge("websocket_notifications.subscriptions.active") == 0 &&
                collector.ObservedTagCount == 0;

            return new PerformanceResult(
                passed,
                options,
                stopwatch.Elapsed.TotalSeconds,
                sourceMessages,
                dispatches,
                notificationsReceived,
                notificationsMatched,
                expectedRecipientMatches,
                messagesEnqueued,
                messagesSent,
                messagesDropped,
                shutdownDiscardedMessages,
                collector.Counter("websocket_notifications.clients.slow_drops"),
                collector.Counter("websocket_notifications.clients.slow_disconnects"),
                collector.Counter("websocket_notifications.messages.send_failures"),
                peakQueueDepth,
                queuedAtEnd,
                cpuSamples.Count == 0 ? 0 : cpuSamples.Average(),
                peakCpuPercent,
                initialWorkingSet,
                peakWorkingSet,
                finalWorkingSet,
                initialManagedMemory,
                peakManagedMemory,
                finalManagedMemory,
                GC.GetTotalAllocatedBytes(precise: true) - initialAllocated,
                collector.ObservedTagCount,
                interrupted,
                restarted);
        }
        finally
        {
            foreach (var server in servers)
            {
                await server.DisposeAsync().ConfigureAwait(false);
            }
        }
    }
}

internal sealed record PerformanceResult(
    bool Passed,
    PerformanceOptions Topology,
    double ElapsedSeconds,
    long SourceMessages,
    long Dispatches,
    long NotificationsReceived,
    long NotificationsMatched,
    long ExpectedRecipientMatches,
    long MessagesEnqueued,
    long MessagesSent,
    long MessagesDropped,
    long ShutdownDiscardedMessages,
    long SlowClientDrops,
    long SlowClientDisconnects,
    long SendFailures,
    long PeakQueueDepth,
    long FinalQueueDepth,
    double AverageCpuPercent,
    double PeakCpuPercent,
    long InitialWorkingSetBytes,
    long PeakWorkingSetBytes,
    long FinalWorkingSetBytes,
    long InitialManagedMemoryBytes,
    long PeakManagedMemoryBytes,
    long FinalManagedMemoryBytes,
    long AllocatedBytes,
    int ObservedMetricTagCount,
    bool SourceInterruptionCompleted,
    bool ServerRestartCompleted);
