using KafkaHighThroughput.Hosting;
using NotificationProducer.Abstractions;
using NotificationProducer.Configuration;
using NotificationProducer.Endpoints;
using NotificationProducer.Kafka;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddOptions<NotificationProducerOptions>()
    .Bind(builder.Configuration.GetSection(NotificationProducerOptions.SectionName))
    .Validate(options => !string.IsNullOrWhiteSpace(options.ProducerName), "ProducerName is required.")
    .Validate(options => !string.IsNullOrWhiteSpace(options.Topic), "Topic is required.")
    .ValidateOnStart();
builder.Services
    .AddKafkaHighThroughput(builder.Configuration)
    .AddProducerClients()
    .AddProducerClient<NotificationProducerClient, string, string>();
builder.Services.AddSingleton<INotificationPublisher>(serviceProvider =>
    serviceProvider.GetRequiredService<NotificationProducerClient>());
builder.Services.AddSingleton(TimeProvider.System);

// The producer is a local demonstration API, so its OpenAPI document and interactive UI
// are available in every environment. Production deployments should apply their own policy.
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();
app.UseSwagger();
app.UseSwaggerUI();
app.MapGet("/", () => Results.Ok(new { service = "NotificationProducer" }));
app.MapNotificationProducerEndpoints();
app.Run();

namespace NotificationProducer
{
    public partial class Program;
}
