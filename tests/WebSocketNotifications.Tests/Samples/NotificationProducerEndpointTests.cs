using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace WebSocketNotifications.Tests.Samples;

public sealed class NotificationProducerEndpointTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 6, 12, 0, 0, TimeSpan.Zero);
    private static readonly string[] CombinedUsers = ["user-1", "user-2"];
    private static readonly string[] OperatorGroup = ["operators"];
    private static readonly string[] MatchFeed = ["match-42"];
    private static readonly string[] ScoreEvent = ["score.changed"];

    [Theory]
    [InlineData("/api/notifications/users", "user:user-1", "users")]
    [InlineData("/api/notifications/groups", "group:operators", "groups")]
    [InlineData("/api/notifications/feeds", "feed:match-42", "feeds")]
    [InlineData("/api/notifications/events", "event:score.changed", "events")]
    public async Task TargetedEndpoint_PublishesOnlyRequestedRoutingDimension(
        string path,
        string expectedKey,
        string expectedDimension)
    {
        var publisher = new RecordingNotificationPublisher();
        await using var app = await StartApplicationAsync(publisher);

        var response = await app.GetTestClient().PostAsJsonAsync(
            path,
            new
            {
                targets = new[] { TargetFor(expectedDimension) },
                payload = new { score = 7 },
            });

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var receipt = await response.Content.ReadFromJsonAsync<PublishNotificationResponse>();
        var notification = Assert.Single(publisher.Notifications);
        Assert.Equal(expectedKey, notification.Key);
        Assert.Equal(7, notification.Payload.GetProperty("score").GetInt32());
        Assert.Equal(Now, notification.CreatedAt);
        Assert.Equal("notifications", receipt!.Topic);
        Assert.Equal(notification.MessageId, receipt.MessageId);
        AssertDimension(notification, expectedDimension);
    }

    [Fact]
    public async Task CombinedEndpoint_PublishesAnyRoutingCombinationAndExplicitKey()
    {
        var publisher = new RecordingNotificationPublisher();
        await using var app = await StartApplicationAsync(publisher);
        var expiresAt = Now.AddMinutes(2);

        var response = await app.GetTestClient().PostAsJsonAsync(
            "/api/notifications",
            new
            {
                userIds = CombinedUsers,
                groups = OperatorGroup,
                feeds = MatchFeed,
                eventTypes = ScoreEvent,
                payload = new { score = 8 },
                expiresAt,
                key = "order:42",
            });

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var notification = Assert.Single(publisher.Notifications);
        Assert.Equal(["user-1", "user-2"], notification.UserIds);
        Assert.Equal(["operators"], notification.Groups);
        Assert.Equal(["match-42"], notification.Feeds);
        Assert.Equal(["score.changed"], notification.EventTypes);
        Assert.Equal("order:42", notification.Key);
        Assert.Equal(expiresAt, notification.ExpiresAt);
    }

    [Fact]
    public async Task TargetedEndpoint_WhenTargetsAreEmpty_ReturnsValidationProblemWithoutPublishing()
    {
        var publisher = new RecordingNotificationPublisher();
        await using var app = await StartApplicationAsync(publisher);

        var response = await app.GetTestClient().PostAsJsonAsync(
            "/api/notifications/users",
            new { targets = Array.Empty<string>(), payload = new { value = 1 } });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(publisher.Notifications);
    }

    [Fact]
    public async Task CombinedEndpoint_WhenPayloadIsMissing_ReturnsValidationProblemWithoutPublishing()
    {
        var publisher = new RecordingNotificationPublisher();
        await using var app = await StartApplicationAsync(publisher);

        var response = await app.GetTestClient().PostAsJsonAsync(
            "/api/notifications",
            new { groups = OperatorGroup });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(publisher.Notifications);
    }

    [Fact]
    public async Task Swagger_ExposesUiAndDocumentsEveryNotificationEndpoint()
    {
        await using var app = await StartApplicationAsync(new RecordingNotificationPublisher());
        var client = app.GetTestClient();

        var ui = await client.GetAsync("/swagger/index.html");
        var json = await client.GetStringAsync("/swagger/v1/swagger.json");
        using var document = JsonDocument.Parse(json);
        var paths = document.RootElement.GetProperty("paths");

        Assert.Equal(HttpStatusCode.OK, ui.StatusCode);
        Assert.True(paths.TryGetProperty("/api/notifications", out _));
        Assert.True(paths.TryGetProperty("/api/notifications/users", out _));
        Assert.True(paths.TryGetProperty("/api/notifications/groups", out _));
        Assert.True(paths.TryGetProperty("/api/notifications/feeds", out _));
        Assert.True(paths.TryGetProperty("/api/notifications/events", out _));
    }

    private static async Task<WebApplication> StartApplicationAsync(INotificationPublisher publisher)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(publisher);
        builder.Services.AddSingleton<INotificationPublisher>(publisher);
        builder.Services.AddSingleton<TimeProvider>(new FixedTimeProvider(Now));
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen();
        var app = builder.Build();
        app.UseSwagger();
        app.UseSwaggerUI();
        app.MapNotificationProducerEndpoints();
        await app.StartAsync();
        return app;
    }

    private static string TargetFor(string dimension) => dimension switch
    {
        "users" => "user-1",
        "groups" => "operators",
        "feeds" => "match-42",
        "events" => "score.changed",
        _ => throw new ArgumentOutOfRangeException(nameof(dimension)),
    };

    private static void AssertDimension(QueuedNotification notification, string dimension)
    {
        Assert.Equal(dimension == "users" ? [TargetFor(dimension)] : [], notification.UserIds);
        Assert.Equal(dimension == "groups" ? [TargetFor(dimension)] : [], notification.Groups);
        Assert.Equal(dimension == "feeds" ? [TargetFor(dimension)] : [], notification.Feeds);
        Assert.Equal(dimension == "events" ? [TargetFor(dimension)] : [], notification.EventTypes);
    }

    private sealed class RecordingNotificationPublisher : INotificationPublisher
    {
        public List<QueuedNotification> Notifications { get; } = [];

        public Task<NotificationPublishReceipt> PublishAsync(
            QueuedNotification notification,
            CancellationToken cancellationToken)
        {
            Notifications.Add(notification);
            return Task.FromResult(new NotificationPublishReceipt("notifications", 2, 41));
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
