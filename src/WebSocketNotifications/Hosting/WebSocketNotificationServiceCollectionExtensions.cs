using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Hosting;
using WebSocketNotifications.Abstractions;
using WebSocketNotifications.Configuration;
using WebSocketNotifications.Connections;
using WebSocketNotifications.Delivery;
using WebSocketNotifications.Protocol;

namespace WebSocketNotifications.Hosting;

/// <summary>Registers WebSocket notification services.</summary>
public static class WebSocketNotificationServiceCollectionExtensions
{
    /// <summary>Registers services using programmatic configuration.</summary>
    /// <param name="services">The application's service collection.</param>
    /// <param name="configure">A callback that configures notification behavior.</param>
    /// <returns>The same service collection for chaining.</returns>
    public static IServiceCollection AddWebSocketNotifications(
        this IServiceCollection services,
        Action<WebSocketNotificationOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);
        AddCoreServices(services);
        services.Configure(configure);
        return services;
    }

    /// <summary>Registers services using a standard configuration section.</summary>
    /// <param name="services">The application's service collection.</param>
    /// <param name="configuration">The configuration section to bind.</param>
    /// <returns>The same service collection for chaining.</returns>
    public static IServiceCollection AddWebSocketNotifications(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        AddCoreServices(services);
        services.Configure<WebSocketNotificationOptions>(configuration);
        return services;
    }

    private static void AddCoreServices(IServiceCollection services)
    {
        services.AddOptions();
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<
                IValidateOptions<WebSocketNotificationOptions>,
                WebSocketNotificationOptionsValidator>());
        services.TryAddSingleton<ConnectionRegistry>();
        services.TryAddSingleton<NotificationRouter>();
        services.TryAddSingleton(serviceProvider =>
        {
            var options = serviceProvider.GetRequiredService<IOptions<WebSocketNotificationOptions>>().Value;
            return new NotificationDispatcher(
                serviceProvider.GetRequiredService<ConnectionRegistry>(),
                serviceProvider.GetRequiredService<NotificationRouter>(),
                options.MaxOutgoingMessageSize);
        });
        services.TryAddSingleton(serviceProvider =>
            new WebSocketNotificationHub(
                serviceProvider.GetRequiredService<ConnectionRegistry>(),
                serviceProvider.GetRequiredService<NotificationDispatcher>(),
                serviceProvider.GetRequiredService<TimeProvider>()));
        // Delay hub construction until the source worker actually needs it. This also breaks
        // the otherwise eager dependency path between hosted-service creation and the hub.
        services.TryAddSingleton(serviceProvider =>
            new Lazy<WebSocketNotificationHub>(
                serviceProvider.GetRequiredService<WebSocketNotificationHub>,
                LazyThreadSafetyMode.ExecutionAndPublication));
        services.TryAddSingleton<ISubscriptionAuthorizer, DenyAllSubscriptionAuthorizer>();
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton(serviceProvider =>
            new ConnectionSession(
                serviceProvider.GetRequiredService<ConnectionRegistry>(),
                serviceProvider.GetRequiredService<ISubscriptionAuthorizer>(),
                serviceProvider.GetService<IWebSocketInboundMessageHandler>(),
                serviceProvider.GetRequiredService<IOptions<WebSocketNotificationOptions>>().Value,
                serviceProvider.GetRequiredService<TimeProvider>(),
                serviceProvider.GetRequiredService<ILogger<ConnectionSession>>()));
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IHostedService, NotificationMessageSourceWorker>());
    }
}
