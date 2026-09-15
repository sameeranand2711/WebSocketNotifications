namespace WebSocketNotifications.Performance;

internal sealed record PerformanceOptions(
    int ServerCount,
    int ConnectionsPerServer,
    int SubscriptionsPerConnection,
    int MessageRate,
    int PayloadBytes,
    int SlowClientPercent,
    int SlowSendDelayMilliseconds,
    int BufferCapacity,
    int ChurnPercent,
    int ChurnIntervalSeconds,
    int DurationSeconds,
    int SourceInterruptionSeconds,
    string? OutputPath)
{
    public static PerformanceOptions Parse(string[] args)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < args.Length; index += 2)
        {
            if (!args[index].StartsWith("--", StringComparison.Ordinal) || index + 1 >= args.Length)
            {
                throw new ArgumentException("Arguments must be supplied as --name value pairs.");
            }

            values.Add(args[index][2..], args[index + 1]);
        }

        var options = new PerformanceOptions(
            GetInt(values, "servers", 2),
            GetInt(values, "connections-per-server", 100),
            GetInt(values, "subscriptions-per-connection", 8),
            GetInt(values, "message-rate", 50),
            GetInt(values, "payload-bytes", 4096),
            GetInt(values, "slow-client-percent", 5),
            GetInt(values, "slow-send-delay-ms", 250),
            GetInt(values, "buffer-capacity", 32),
            GetInt(values, "churn-percent", 5),
            GetInt(values, "churn-interval-seconds", 15),
            GetInt(values, "duration-seconds", 60),
            GetInt(values, "source-interruption-seconds", 3),
            values.GetValueOrDefault("output"));

        options.Validate();
        return options;
    }

    private void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(ServerCount, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(ServerCount, 16);
        ArgumentOutOfRangeException.ThrowIfLessThan(ConnectionsPerServer, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(ConnectionsPerServer, 10_000);
        ArgumentOutOfRangeException.ThrowIfLessThan(SubscriptionsPerConnection, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(SubscriptionsPerConnection, 128);
        ArgumentOutOfRangeException.ThrowIfLessThan(MessageRate, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(MessageRate, 10_000);
        ArgumentOutOfRangeException.ThrowIfLessThan(PayloadBytes, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(PayloadBytes, 256 * 1024);
        ArgumentOutOfRangeException.ThrowIfNegative(SlowClientPercent);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(SlowClientPercent, 100);
        ArgumentOutOfRangeException.ThrowIfNegative(SlowSendDelayMilliseconds);
        ArgumentOutOfRangeException.ThrowIfLessThan(BufferCapacity, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(BufferCapacity, 10_000);
        ArgumentOutOfRangeException.ThrowIfNegative(ChurnPercent);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(ChurnPercent, 100);
        ArgumentOutOfRangeException.ThrowIfLessThan(ChurnIntervalSeconds, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(DurationSeconds, 10);
        ArgumentOutOfRangeException.ThrowIfNegative(SourceInterruptionSeconds);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(SourceInterruptionSeconds, DurationSeconds);
    }

    private static int GetInt(Dictionary<string, string> values, string name, int defaultValue) =>
        values.TryGetValue(name, out var value) && int.TryParse(value, out var parsed)
            ? parsed
            : values.ContainsKey(name)
                ? throw new ArgumentException($"--{name} must be an integer.")
                : defaultValue;
}
