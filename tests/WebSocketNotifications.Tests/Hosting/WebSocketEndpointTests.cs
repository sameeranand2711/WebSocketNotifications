using System.Net;
using System.Net.WebSockets;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace WebSocketNotifications.Tests.Hosting;

public sealed class WebSocketEndpointTests
{
    [Fact]
    public async Task Endpoint_WhenRequestIsAnonymous_ReturnsUnauthorized()
    {
        await using var server = await CreateServerAsync();

        var response = await server.GetTestClient().GetAsync("/ws/notifications");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Endpoint_WhenAuthenticatedRequestIsNotWebSocket_ReturnsBadRequest()
    {
        await using var server = await CreateServerAsync();
        var request = new HttpRequestMessage(HttpMethod.Get, "/ws/notifications");
        request.Headers.Add("X-Test-User", "user-1");

        var response = await server.GetTestClient().SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Endpoint_UsesConfiguredPath()
    {
        await using var server = await CreateServerAsync(endpointPath: "/custom-notifications");
        var client = server.GetTestClient();
        var oldRequest = new HttpRequestMessage(HttpMethod.Get, "/ws/notifications");
        oldRequest.Headers.Add("X-Test-User", "user-1");
        var customRequest = new HttpRequestMessage(HttpMethod.Get, "/custom-notifications");
        customRequest.Headers.Add("X-Test-User", "user-1");

        var oldResponse = await client.SendAsync(oldRequest);
        var customResponse = await client.SendAsync(customRequest);

        Assert.Equal(HttpStatusCode.NotFound, oldResponse.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, customResponse.StatusCode);
    }

    [Fact]
    public async Task Endpoint_WhenIdentityCannotBeResolved_RejectsWebSocketUpgrade()
    {
        await using var server = await CreateServerAsync(resolveIdentity: false);
        var client = server.GetTestServer().CreateWebSocketClient();
        client.ConfigureRequest = request => request.Headers["X-Test-User"] = "user-1";

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.ConnectAsync(new Uri("ws://localhost/ws/notifications"), CancellationToken.None));
        Assert.Contains("401", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Endpoint_WhenClientConnectsAndCloses_RegistersThenCleansUpConnection()
    {
        await using var server = await CreateServerAsync();
        var client = server.GetTestServer().CreateWebSocketClient();
        client.ConfigureRequest = request => request.Headers["X-Test-User"] = "user-1";
        using var socket = await client.ConnectAsync(
            new Uri("ws://localhost/ws/notifications"),
            CancellationToken.None);
        var registry = server.Services.GetRequiredService<ConnectionRegistry>();

        await WaitUntilAsync(() => registry.Count == 1);
        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None);
        await WaitUntilAsync(() => registry.Count == 0);

        Assert.Empty(registry.GetConnectionIdsForUser("user-1"));
    }

    [Fact]
    public async Task Endpoint_DirectNotificationsReachAllUserConnectionsInPublishOrder()
    {
        await using var server = await CreateServerAsync();
        using var first = await ConnectAsync(server, "user-1");
        using var second = await ConnectAsync(server, "user-1");
        var registry = server.Services.GetRequiredService<ConnectionRegistry>();
        await WaitUntilAsync(() => registry.Count == 2);
        var hub = server.Services.GetRequiredService<WebSocketNotificationHub>();

        await hub.PublishAsync(CreateNotification("message-1", userIds: ["user-1"]));
        await hub.PublishAsync(CreateNotification("message-2", userIds: ["user-1"]));

        Assert.Equal(["message-1", "message-2"], await ReceiveMessageIdsAsync(first, 2));
        Assert.Equal(["message-1", "message-2"], await ReceiveMessageIdsAsync(second, 2));
        await first.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None);
        await second.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None);
    }

    [Fact]
    public async Task Endpoint_AuthorizedOpaqueSubscriptionConfirmsAndReceivesNotification()
    {
        await using var server = await CreateServerAsync();
        using var socket = await ConnectAsync(server, "user-1");
        await socket.SendAsync(
            """{"type":"subscribe","requestId":"r1","subscriptions":["tenant:abc:group:operators"]}"""u8.ToArray(),
            WebSocketMessageType.Text,
            endOfMessage: true,
            CancellationToken.None);

        using var confirmation = JsonDocument.Parse(await ReceiveTextAsync(socket));
        Assert.Equal("subscribed", confirmation.RootElement.GetProperty("type").GetString());

        await server.Services.GetRequiredService<WebSocketNotificationHub>().PublishAsync(
            CreateNotification("subscription-message", subscriptions: ["tenant:abc:group:operators"]));

        using var notification = JsonDocument.Parse(await ReceiveTextAsync(socket));
        Assert.Equal("subscription-message", notification.RootElement.GetProperty("messageId").GetString());
        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None);
    }

    [Fact]
    public async Task MultiServer_SameUserConnectedToBothHosts_EachConnectionReceivesOneCopy()
    {
        await using var firstServer = await CreateServerAsync();
        await using var secondServer = await CreateServerAsync();
        using var firstSocket = await ConnectAsync(firstServer, "user-1");
        using var secondSocket = await ConnectAsync(secondServer, "user-1");

        await PublishToAllAsync(
            CreateNotification("message-1", userIds: ["user-1"]),
            firstServer,
            secondServer);

        Assert.Equal("message-1", await ReceiveMessageIdAsync(firstSocket));
        Assert.Equal("message-1", await ReceiveMessageIdAsync(secondSocket));
        await AssertNoMessageAsync(firstSocket);
        await AssertNoMessageAsync(secondSocket);
        await CloseAsync(firstSocket, secondSocket);
    }

    [Fact]
    public async Task MultiServer_UsersSplitAcrossHosts_OnlyIntendedUserReceives()
    {
        await using var firstServer = await CreateServerAsync();
        await using var secondServer = await CreateServerAsync();
        using var firstSocket = await ConnectAsync(firstServer, "user-1");
        using var secondSocket = await ConnectAsync(secondServer, "user-2");

        await PublishToAllAsync(
            CreateNotification("for-user-1", userIds: ["user-1"]),
            firstServer,
            secondServer);

        Assert.Equal("for-user-1", await ReceiveMessageIdAsync(firstSocket));
        await AssertNoMessageAsync(secondSocket);

        await PublishToAllAsync(
            CreateNotification("for-user-2", userIds: ["user-2"]),
            firstServer,
            secondServer);

        Assert.Equal("for-user-2", await ReceiveMessageIdAsync(secondSocket));
        await AssertNoMessageAsync(firstSocket);
        await CloseAsync(firstSocket, secondSocket);
    }

    [Fact]
    public async Task MultiServer_SameSubscriptionOnBothHosts_EachConnectionReceives()
    {
        await using var firstServer = await CreateServerAsync();
        await using var secondServer = await CreateServerAsync();
        using var firstSocket = await ConnectAsync(firstServer, "user-1");
        using var secondSocket = await ConnectAsync(secondServer, "user-2");
        const string subscription = "tenant:abc:channel:alerts";
        await SubscribeAsync(firstSocket, subscription, "first");
        await SubscribeAsync(secondSocket, subscription, "second");

        await PublishToAllAsync(
            CreateNotification("subscription-message", subscriptions: [subscription]),
            firstServer,
            secondServer);

        Assert.Equal("subscription-message", await ReceiveMessageIdAsync(firstSocket));
        Assert.Equal("subscription-message", await ReceiveMessageIdAsync(secondSocket));
        await CloseAsync(firstSocket, secondSocket);
    }

    [Fact]
    public async Task MultiServer_UserAndSubscriptionMatch_OnePhysicalConnectionGetsOneCopy()
    {
        await using var firstServer = await CreateServerAsync();
        await using var secondServer = await CreateServerAsync();
        using var firstSocket = await ConnectAsync(firstServer, "user-1");
        using var secondSocket = await ConnectAsync(secondServer, "user-1");
        const string subscription = "tenant:abc:role:operator";
        await SubscribeAsync(firstSocket, subscription, "first");
        await SubscribeAsync(secondSocket, subscription, "second");

        await PublishToAllAsync(
            CreateNotification(
                "combined-message",
                userIds: ["user-1"],
                subscriptions: [subscription]),
            firstServer,
            secondServer);

        Assert.Equal("combined-message", await ReceiveMessageIdAsync(firstSocket));
        Assert.Equal("combined-message", await ReceiveMessageIdAsync(secondSocket));
        await AssertNoMessageAsync(firstSocket);
        await AssertNoMessageAsync(secondSocket);
        await CloseAsync(firstSocket, secondSocket);
    }

    [Fact]
    public async Task MultiServer_HostWithNoLocalMatch_DoesNotSend()
    {
        await using var firstServer = await CreateServerAsync();
        await using var secondServer = await CreateServerAsync();
        using var firstSocket = await ConnectAsync(firstServer, "target-user");
        using var secondSocket = await ConnectAsync(secondServer, "other-user");

        await PublishToAllAsync(
            CreateNotification("targeted-message", userIds: ["target-user"]),
            firstServer,
            secondServer);

        Assert.Equal("targeted-message", await ReceiveMessageIdAsync(firstSocket));
        await AssertNoMessageAsync(secondSocket);
        await CloseAsync(firstSocket, secondSocket);
    }

    [Fact]
    public async Task MultiServer_ConnectionLeavesOneHost_OtherHostContinuesDelivery()
    {
        await using var firstServer = await CreateServerAsync();
        await using var secondServer = await CreateServerAsync();
        using var firstSocket = await ConnectAsync(firstServer, "user-1");
        using var secondSocket = await ConnectAsync(secondServer, "user-1");
        await firstSocket.CloseAsync(
            WebSocketCloseStatus.NormalClosure,
            "disconnect",
            CancellationToken.None);
        await WaitUntilAsync(
            () => firstServer.Services.GetRequiredService<ConnectionRegistry>().Count == 0);

        await PublishToAllAsync(
            CreateNotification("after-disconnect", userIds: ["user-1"]),
            firstServer,
            secondServer);

        Assert.Equal("after-disconnect", await ReceiveMessageIdAsync(secondSocket));
        await secondSocket.CloseAsync(
            WebSocketCloseStatus.NormalClosure,
            "done",
            CancellationToken.None);
    }

    private static async Task<WebApplication> CreateServerAsync(
        string endpointPath = "/ws/notifications",
        bool resolveIdentity = true)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddRouting();
        builder.Services.AddAuthentication("Test")
            .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>("Test", _ => { });
        builder.Services.AddAuthorization();
        builder.Services.AddSingleton<IWebSocketUserResolver>(
            new ClaimUserResolver(resolveIdentity));
        builder.Services.AddSingleton<ISubscriptionAuthorizer, AllowAllAuthorizer>();
        builder.Services.AddWebSocketNotifications(options =>
        {
            options.EndpointPath = endpointPath;
            options.HeartbeatEnabled = false;
        });

        var app = builder.Build();
        app.UseRouting();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseWebSockets();
        app.MapWebSocketNotifications();
        await app.StartAsync();
        return app;
    }

    private static async Task<WebSocket> ConnectAsync(WebApplication server, string userId)
    {
        var client = server.GetTestServer().CreateWebSocketClient();
        client.ConfigureRequest = request => request.Headers["X-Test-User"] = userId;
        return await client.ConnectAsync(
            new Uri("ws://localhost/ws/notifications"),
            CancellationToken.None);
    }

    private static NotificationEnvelope CreateNotification(
        string messageId,
        IEnumerable<string>? userIds = null,
        IEnumerable<string>? subscriptions = null) =>
        new(
            messageId,
            JsonSerializer.SerializeToElement(new { text = messageId }),
            DateTimeOffset.UtcNow,
            userIds: userIds,
            subscriptions: subscriptions);

    private static async Task<IReadOnlyList<string>> ReceiveMessageIdsAsync(WebSocket socket, int count)
    {
        var messageIds = new List<string>(count);
        for (var index = 0; index < count; index++)
        {
            using var document = JsonDocument.Parse(await ReceiveTextAsync(socket));
            messageIds.Add(document.RootElement.GetProperty("messageId").GetString()!);
        }

        return messageIds;
    }

    private static async Task<string> ReceiveMessageIdAsync(WebSocket socket)
    {
        using var document = JsonDocument.Parse(await ReceiveTextAsync(socket));
        return document.RootElement.GetProperty("messageId").GetString()!;
    }

    private static async Task AssertNoMessageAsync(WebSocket socket)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));
        var buffer = new byte[1];
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => socket.ReceiveAsync(buffer, timeout.Token));
    }

    private static async Task SubscribeAsync(
        WebSocket socket,
        string subscription,
        string requestId)
    {
        var request = JsonSerializer.SerializeToUtf8Bytes(
            new
            {
                type = "subscribe",
                requestId,
                subscriptions = new[] { subscription },
            });
        await socket.SendAsync(
            request,
            WebSocketMessageType.Text,
            endOfMessage: true,
            CancellationToken.None);
        using var confirmation = JsonDocument.Parse(await ReceiveTextAsync(socket));
        Assert.Equal("subscribed", confirmation.RootElement.GetProperty("type").GetString());
    }

    private static Task PublishToAllAsync(
        NotificationEnvelope notification,
        params WebApplication[] servers) =>
        Task.WhenAll(
            servers.Select(server =>
                server.Services
                    .GetRequiredService<WebSocketNotificationHub>()
                    .PublishAsync(notification)
                    .AsTask()));

    private static Task CloseAsync(params WebSocket[] sockets) =>
        Task.WhenAll(
            sockets.Select(socket =>
                socket.CloseAsync(
                    WebSocketCloseStatus.NormalClosure,
                    "done",
                    CancellationToken.None)));

    private static async Task<byte[]> ReceiveTextAsync(WebSocket socket)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        using var message = new MemoryStream();
        var buffer = new byte[1024];
        WebSocketReceiveResult result;
        do
        {
            result = await socket.ReceiveAsync(buffer, timeout.Token);
            message.Write(buffer, 0, result.Count);
        }
        while (!result.EndOfMessage);

        return message.ToArray();
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(2);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }

        Assert.True(condition());
    }

    private sealed class ClaimUserResolver(bool resolveIdentity) : IWebSocketUserResolver
    {
        public ValueTask<string?> ResolveUserIdAsync(
            HttpContext context,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(
                resolveIdentity ? context.User.FindFirstValue(ClaimTypes.NameIdentifier) : null);
    }

    private sealed class AllowAllAuthorizer : ISubscriptionAuthorizer
    {
        public ValueTask<bool> AuthorizeAsync(
            SubscriptionAuthorizationContext context,
            CancellationToken cancellationToken) => ValueTask.FromResult(true);
    }

    private sealed class TestAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue("X-Test-User", out var userId))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var identity = new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, userId.ToString())],
                Scheme.Name);
            return Task.FromResult(
                AuthenticateResult.Success(
                    new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
        }
    }
}
