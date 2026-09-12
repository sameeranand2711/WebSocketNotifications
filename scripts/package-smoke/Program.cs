using System.Net;
using System.Net.WebSockets;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Options;
using WebSocketNotifications.Abstractions;
using WebSocketNotifications.Contracts;
using WebSocketNotifications.Delivery;
using WebSocketNotifications.Hosting;

const string scheme = "PackageSmoke";
const string userId = "package-user";
const string messageId = "package-smoke-message";
const string endpointPath = "/ws/package-smoke";

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0));
builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
builder.Services.AddAuthentication(scheme)
    .AddScheme<AuthenticationSchemeOptions, HeaderAuthenticationHandler>(scheme, _ => { });
builder.Services.AddAuthorization();
builder.Services.AddSingleton<IWebSocketUserResolver, ClaimUserResolver>();
builder.Services.AddSingleton<ISubscriptionAuthorizer, AllowAllSubscriptions>();
builder.Services.AddWebSocketNotifications(options =>
{
    options.EndpointPath = endpointPath;
    options.HeartbeatEnabled = false;
});

await using var app = builder.Build();
app.UseWebSockets();
app.UseAuthentication();
app.UseAuthorization();
app.MapWebSocketNotifications();

await app.StartAsync();
try
{
    var addresses = app.Services
        .GetRequiredService<IServer>()
        .Features
        .Get<IServerAddressesFeature>()?
        .Addresses;
    var httpAddress = addresses?.Single()
        ?? throw new InvalidOperationException("The smoke-test server did not publish one address.");
    var webSocketAddress = new UriBuilder(httpAddress)
    {
        Scheme = "ws",
        Path = endpointPath,
    }.Uri;

    using var socket = new ClientWebSocket();
    socket.Options.SetRequestHeader(HeaderAuthenticationHandler.UserHeader, userId);
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
    await socket.ConnectAsync(webSocketAddress, timeout.Token);

    using var payload = JsonDocument.Parse("""{"source":"packed-package"}""");
    var notification = new NotificationEnvelope(
        messageId,
        payload.RootElement,
        DateTimeOffset.UtcNow,
        userIds: [userId]);
    await app.Services
        .GetRequiredService<WebSocketNotificationHub>()
        .PublishAsync(notification, timeout.Token);

    var buffer = new byte[4096];
    var result = await socket.ReceiveAsync(buffer, timeout.Token);
    if (result.MessageType != WebSocketMessageType.Text || !result.EndOfMessage)
    {
        throw new InvalidOperationException("The package smoke test did not receive one complete text frame.");
    }

    using var frame = JsonDocument.Parse(Encoding.UTF8.GetString(buffer, 0, result.Count));
    var root = frame.RootElement;
    if (root.GetProperty("type").GetString() != "notification"
        || root.GetProperty("messageId").GetString() != messageId)
    {
        throw new InvalidOperationException(
            $"The package smoke test received an unexpected frame: {Encoding.UTF8.GetString(buffer, 0, result.Count)}");
    }

    await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "complete", timeout.Token);
    Console.WriteLine("PACKED PACKAGE SMOKE TEST PASSED");
}
finally
{
    await app.StopAsync();
}

internal sealed class HeaderAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string UserHeader = "X-Smoke-User";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var userId = Request.Headers[UserHeader].ToString();
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, userId)],
            Scheme.Name);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}

internal sealed class ClaimUserResolver : IWebSocketUserResolver
{
    public ValueTask<string?> ResolveUserIdAsync(
        HttpContext context,
        CancellationToken cancellationToken) =>
        ValueTask.FromResult(context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value);
}

internal sealed class AllowAllSubscriptions : ISubscriptionAuthorizer
{
    public ValueTask<bool> AuthorizeAsync(
        SubscriptionAuthorizationContext context,
        CancellationToken cancellationToken) =>
        ValueTask.FromResult(true);
}
