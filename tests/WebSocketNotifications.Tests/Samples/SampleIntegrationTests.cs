using System.Text.Json;
using Confluent.Kafka;
using Microsoft.Extensions.Options;
using Xunit;

namespace WebSocketNotifications.Tests.Samples;

public sealed class SampleIntegrationTests
{
    [Fact]
    public async Task KafkaConsumer_WhenRecordIsValid_AwaitsNeutralSourceHandler()
    {
        var source = CreateSource();
        var handled = new TaskCompletionSource<NotificationEnvelope>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellation = new CancellationTokenSource();
        var sourceRun = source.RunAsync(
            (notification, _) =>
            {
                handled.TrySetResult(notification);
                return Task.CompletedTask;
            },
            cancellation.Token);
        var consumer = new KafkaNotificationConsumer(source);

        await consumer.HandleAsync(
            CreateKafkaRecord("""
                {
                  "messageId":"message-1",
                  "userIds":["user-1"],
                  "subscriptions":["tenant:abc:event:score.changed"],
                  "payload":{"score":7},
                  "createdAt":"2026-01-01T00:00:00Z"
                }
                """),
            CancellationToken.None);

        var notification = await handled.Task;
        Assert.Equal("message-1", notification.MessageId);
        Assert.Equal(["user-1"], notification.UserIds);
        Assert.Equal(["tenant:abc:event:score.changed"], notification.Subscriptions);
        Assert.Equal(7, notification.Payload.GetProperty("score").GetInt32());
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sourceRun);
    }

    [Fact]
    public async Task KafkaConsumer_WhenNeutralHandlerFails_PropagatesFailureToKafkaHandler()
    {
        var source = CreateSource();
        using var cancellation = new CancellationTokenSource();
        var sourceRun = source.RunAsync(
            (_, _) => Task.FromException(new InvalidOperationException("delivery rejected")),
            cancellation.Token);
        var consumer = new KafkaNotificationConsumer(source);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            consumer.HandleAsync(
                CreateKafkaRecord("""
                    {
                      "messageId":"message-1",
                      "userIds":["user-1"],
                      "payload":{"value":1},
                      "createdAt":"2026-01-01T00:00:00Z"
                    }
                    """),
                CancellationToken.None));

        Assert.Equal("delivery rejected", error.Message);
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sourceRun);
    }

    [Fact]
    public async Task KafkaConsumer_WhenRecordJsonIsInvalid_LeavesDeserializationWithAdapter()
    {
        var consumer = new KafkaNotificationConsumer(CreateSource());

        await Assert.ThrowsAsync<JsonException>(() =>
            consumer.HandleAsync(CreateKafkaRecord("{"), CancellationToken.None));
    }

    private static KafkaNotificationMessageSource CreateSource() =>
        new(Options.Create(new KafkaAdapterOptions { ChannelCapacity = 4 }));

    private static ConsumeResult<string, string> CreateKafkaRecord(string json) =>
        new()
        {
            Message = new Message<string, string>
            {
                Key = "user:user-1",
                Value = json,
            },
        };
}
