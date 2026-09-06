using KafkaHighThroughput.Hosting.Extensions;
using Microsoft.AspNetCore.Authentication;
using WebSocketNotifications.Abstractions;
using WebSocketNotifications.Configuration;
using WebSocketNotifications.Host.Authentication;
using WebSocketNotifications.Host.Kafka;
using WebSocketNotifications.Host.WebSockets;
using WebSocketNotifications.Hosting;

var builder = WebApplication.CreateBuilder(args);

// This query-based identity provider keeps the sample self-contained. Replace it with the
// application's normal cookie, bearer-token, or other authentication scheme in production.
builder.Services.AddAuthentication(SampleAuthenticationHandler.SchemeName)
    .AddScheme<AuthenticationSchemeOptions, SampleAuthenticationHandler>(
        SampleAuthenticationHandler.SchemeName,
        _ => { });
builder.Services.AddAuthorization();
builder.Services.AddSingleton<IWebSocketUserResolver, ClaimUserResolver>();
builder.Services.AddSingleton<ISubscriptionAuthorizer, SampleSubscriptionAuthorizer>();
builder.Services.AddSingleton<IWebSocketInboundMessageHandler, SampleInboundMessageHandler>();
builder.Services.AddWebSocketNotifications(
    builder.Configuration.GetSection(WebSocketNotificationOptions.SectionName));

// Kafka is a sample adapter outside the provider-neutral library. Its bounded channel carries
// backpressure from local routing to the Kafka consumer workers.
builder.Services.AddOptions<KafkaAdapterOptions>()
    .Bind(builder.Configuration.GetSection(KafkaAdapterOptions.SectionName))
    .Validate(options => options.ChannelCapacity is >= 1 and <= 10_000, "ChannelCapacity must be between 1 and 10000.")
    .ValidateOnStart();
builder.Services.AddSingleton<KafkaNotificationMessageSource>();
builder.Services.AddSingleton<INotificationMessageSource>(serviceProvider =>
    serviceProvider.GetRequiredService<KafkaNotificationMessageSource>());
builder.Services.AddKafkaConsumerWorkers(builder.Configuration);
builder.Services.AddKafkaTopicConsumer<KafkaNotificationConsumer, string, string>();

var app = builder.Build();
app.UseWebSockets();
app.UseAuthentication();
app.UseAuthorization();
app.MapGet("/", () => Results.Ok(new { service = "WebSocketNotifications.Host" })).AllowAnonymous();
app.MapWebSocketNotifications();
app.Run();

namespace WebSocketNotifications.Host
{
    public partial class Program;
}
