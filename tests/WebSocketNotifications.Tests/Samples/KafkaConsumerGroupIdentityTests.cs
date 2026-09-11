using Xunit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace WebSocketNotifications.Tests.Samples;

public sealed class KafkaConsumerGroupIdentityTests
{
    [Fact]
    public void Create_WithExplicitInstanceId_NamespacesConsumerGroup()
    {
        var options = new KafkaAdapterOptions
        {
            ApplicationName = "websocket-notifications",
            InstanceId = "pod-7",
        };

        var identity = KafkaConsumerGroupIdentity.Create(options, "Production");

        Assert.Equal("pod-7", identity.InstanceId);
        Assert.Equal("websocket-notifications.Production.pod-7", identity.GroupId);
    }

    [Fact]
    public void Create_WithoutInstanceId_UsesEphemeralProcessIdentity()
    {
        var options = new KafkaAdapterOptions
        {
            ApplicationName = "websocket-notifications",
        };

        var identity = KafkaConsumerGroupIdentity.Create(
            options,
            "Staging",
            () => "process-42");

        Assert.Equal("process-42", identity.InstanceId);
        Assert.Equal("websocket-notifications.Staging.process-42", identity.GroupId);
    }

    [Fact]
    public void Create_WithoutInstanceId_GeneratesDistinctProcessGroups()
    {
        var options = new KafkaAdapterOptions
        {
            ApplicationName = "websocket-notifications",
        };

        var first = KafkaConsumerGroupIdentity.Create(options, "Production");
        var second = KafkaConsumerGroupIdentity.Create(options, "Production");

        Assert.NotEqual(first.InstanceId, second.InstanceId);
        Assert.NotEqual(first.GroupId, second.GroupId);
    }

    [Fact]
    public void ApplyToConfiguration_SetsIndependentLiveOnlyConsumerGroup()
    {
        var values = new Dictionary<string, string?>
        {
            ["KafkaConsumerWorkers:Consumers:0:Name"] = "another-consumer",
            ["KafkaConsumerWorkers:Consumers:0:GroupId"] = "unchanged",
            ["KafkaConsumerWorkers:Consumers:1:Name"] = KafkaNotificationConsumer.Name,
            ["KafkaConsumerWorkers:Consumers:1:GroupId"] = "fixed-shared-group",
            ["KafkaConsumerWorkers:Consumers:1:AutoOffsetReset"] = "Earliest",
        };
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
        var identity = new KafkaConsumerGroupIdentity(
            "instance-a",
            "notifications.Production.instance-a");

        KafkaConsumerConfiguration.Apply(configuration, identity);

        Assert.Equal("unchanged", configuration["KafkaConsumerWorkers:Consumers:0:GroupId"]);
        Assert.Equal(
            "notifications.Production.instance-a",
            configuration["KafkaConsumerWorkers:Consumers:1:GroupId"]);
        Assert.Equal("Latest", configuration["KafkaConsumerWorkers:Consumers:1:AutoOffsetReset"]);
    }

    [Fact]
    public void ApplyToConfiguration_WhenNamedConsumerIsMissing_FailsStartup()
    {
        var configuration = new ConfigurationBuilder().Build();
        var identity = new KafkaConsumerGroupIdentity(
            "instance-a",
            "notifications.Production.instance-a");

        var error = Assert.Throws<InvalidOperationException>(
            () => KafkaConsumerConfiguration.Apply(configuration, identity));

        Assert.Contains(KafkaNotificationConsumer.Name, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AddKafkaSourceReadiness_RegistersAssignmentAwareReadyCheck()
    {
        var services = new ServiceCollection();

        KafkaSourceReadiness.Add(services);

        using var provider = services.BuildServiceProvider();
        var registrations = provider
            .GetRequiredService<IOptions<HealthCheckServiceOptions>>()
            .Value
            .Registrations;
        var registration = Assert.Single(
            registrations,
            candidate => candidate.Name == "kafka_consumers");
        Assert.Contains("ready", registration.Tags);
        Assert.Equal(HealthStatus.Unhealthy, registration.FailureStatus);
        Assert.Equal("/health/ready", KafkaSourceReadiness.EndpointPath);
    }
}
