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
        using var server = CreateServer();

        var response = await server.CreateClient().GetAsync("/ws/notifications");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Endpoint_WhenAuthenticatedRequestIsNotWebSocket_ReturnsBadRequest()
    {
        using var server = CreateServer();
        var request = new HttpRequestMessage(HttpMethod.Get, "/ws/notifications");
        request.Headers.Add("X-Test-User", "user-1");

        var response = await server.CreateClient().SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Endpoint_UsesConfiguredPath()
    {
        using var server = CreateServer(endpointPath: "/custom-notifications");
        var client = server.CreateClient();
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
        using var server = CreateServer(resolveIdentity: false);
        var client = server.CreateWebSocketClient();
        client.ConfigureRequest = request => request.Headers["X-Test-User"] = "user-1";

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.ConnectAsync(new Uri("ws://localhost/ws/notifications"), CancellationToken.None));
        Assert.Contains("401", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Endpoint_WhenClientConnectsAndCloses_RegistersThenCleansUpConnection()
    {
        using var server = CreateServer();
        var client = server.CreateWebSocketClient();
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
        using var server = CreateServer();
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
    public async Task Endpoint_AuthorizedGroupSubscriptionConfirmsAndReceivesNotification()
    {
        using var server = CreateServer();
        using var socket = await ConnectAsync(server, "user-1");
        await socket.SendAsync(
            """{"type":"subscribe","requestId":"r1","kind":"group","value":"operators"}"""u8.ToArray(),
            WebSocketMessageType.Text,
            endOfMessage: true,
            CancellationToken.None);

        using var confirmation = JsonDocument.Parse(await ReceiveTextAsync(socket));
        Assert.Equal("subscribed", confirmation.RootElement.GetProperty("type").GetString());

        await server.Services.GetRequiredService<WebSocketNotificationHub>().PublishAsync(
            CreateNotification("group-message", groups: ["operators"]));

        using var notification = JsonDocument.Parse(await ReceiveTextAsync(socket));
        Assert.Equal("group-message", notification.RootElement.GetProperty("messageId").GetString());
        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None);
    }

    private static TestServer CreateServer(
        string endpointPath = "/ws/notifications",
        bool resolveIdentity = true)
    {
        var builder = new WebHostBuilder()
            .ConfigureServices(services =>
            {
                services.AddRouting();
                services.AddAuthentication("Test")
                    .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>("Test", _ => { });
                services.AddAuthorization();
                services.AddSingleton<IWebSocketUserResolver>(
                    new ClaimUserResolver(resolveIdentity));
                services.AddSingleton<ISubscriptionAuthorizer, AllowAllAuthorizer>();
                services.AddWebSocketNotifications(options =>
                {
                    options.EndpointPath = endpointPath;
                    options.HeartbeatEnabled = false;
                });
            })
            .Configure(app =>
            {
                app.UseRouting();
                app.UseAuthentication();
                app.UseAuthorization();
                app.UseWebSockets();
                app.UseEndpoints(endpoints => endpoints.MapWebSocketNotifications());
            });

        return new TestServer(builder);
    }

    private static async Task<WebSocket> ConnectAsync(TestServer server, string userId)
    {
        var client = server.CreateWebSocketClient();
        client.ConfigureRequest = request => request.Headers["X-Test-User"] = userId;
        return await client.ConnectAsync(
            new Uri("ws://localhost/ws/notifications"),
            CancellationToken.None);
    }

    private static NotificationEnvelope CreateNotification(
        string messageId,
        IEnumerable<string>? userIds = null,
        IEnumerable<string>? groups = null) =>
        new(
            messageId,
            JsonSerializer.SerializeToElement(new { text = messageId }),
            DateTimeOffset.UtcNow,
            userIds: userIds,
            groups: groups);

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
