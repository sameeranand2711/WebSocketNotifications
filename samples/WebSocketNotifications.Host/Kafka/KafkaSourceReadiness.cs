using KafkaHighThroughput.Hosting.Extensions;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace WebSocketNotifications.Host.Kafka;

/// <summary>Configures source readiness from Kafka consumer health and partition assignment.</summary>
internal static class KafkaSourceReadiness
{
    /// <summary>Gets the source-readiness endpoint path.</summary>
    public const string EndpointPath = "/health/ready";

    /// <summary>Adds the Kafka consumer readiness check.</summary>
    public static void Add(IServiceCollection services)
    {
        services.AddKafkaHighThroughputHealthChecks(options =>
            options.RequireConsumerAssignment = true);
    }

    /// <summary>Maps an endpoint containing only checks tagged for readiness.</summary>
    public static void Map(WebApplication app)
    {
        app.MapHealthChecks(
                EndpointPath,
                new HealthCheckOptions
                {
                    Predicate = registration => registration.Tags.Contains("ready"),
                    ResultStatusCodes =
                    {
                        [HealthStatus.Healthy] = StatusCodes.Status200OK,
                        [HealthStatus.Degraded] = StatusCodes.Status503ServiceUnavailable,
                        [HealthStatus.Unhealthy] = StatusCodes.Status503ServiceUnavailable,
                    },
                })
            .AllowAnonymous();
    }
}
