using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Prosody;

/// <summary>
/// Extension methods for configuring Prosody with dependency injection.
/// </summary>
public static class ProsodyServiceCollectionExtensions
{
    /// <summary>
    /// Adds Prosody logging integration to the service collection.
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <remarks>
    /// <para>
    /// This method registers a hosted service that automatically configures Prosody logging
    /// when the host starts and cleans up when the host stops.
    /// </para>
    /// <para>
    /// The logging configuration uses the <see cref="ILoggerFactory"/> registered in the
    /// dependency injection container.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// var builder = WebApplication.CreateBuilder(args);
    /// builder.Services.AddProsodyLogging();
    /// </code>
    /// </example>
    public static IServiceCollection AddProsodyLogging(this IServiceCollection services)
    {
        services.AddHostedService<ProsodyLoggingHostedService>();
        return services;
    }

    /// <summary>
    /// Adds a <see cref="ProsodyClient"/> to the service collection using configuration from an <see cref="IConfiguration"/> section.
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    /// <param name="configuration">The configuration section containing Prosody client options.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <remarks>
    /// <para>
    /// The configuration section should contain properties that match <see cref="ClientOptions"/>.
    /// </para>
    /// <para>
    /// The client is registered as a singleton because it manages Kafka connections and internal state
    /// that should be shared across the application.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// // appsettings.json:
    /// // {
    /// //   "Prosody": {
    /// //     "BootstrapServers": ["localhost:9092"],
    /// //     "GroupId": "my-app",
    /// //     "SubscribedTopics": ["orders"],
    /// //     "Mode": "Pipeline"
    /// //   }
    /// // }
    ///
    /// var builder = WebApplication.CreateBuilder(args);
    /// builder.Services.AddProsodyClient(builder.Configuration.GetSection("Prosody"));
    /// </code>
    /// </example>
    public static IServiceCollection AddProsodyClient(
        this IServiceCollection services,
        IConfiguration configuration
    )
    {
        return AddProsodyClient(services, configuration, configure: null);
    }

    /// <summary>
    /// Adds a <see cref="ProsodyClient"/> to the service collection using configuration from an <see cref="IConfiguration"/> section
    /// with additional builder customization.
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    /// <param name="configuration">The configuration section containing Prosody client options.</param>
    /// <param name="configure">An optional action to further configure the client builder after binding configuration.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <remarks>
    /// <para>
    /// The configuration section should contain properties that match <see cref="ClientOptions"/>.
    /// The <paramref name="configure"/> action is called after configuration is bound, allowing
    /// programmatic overrides or additions.
    /// </para>
    /// <para>
    /// The client is registered as a singleton because it manages Kafka connections and internal state
    /// that should be shared across the application.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// var builder = WebApplication.CreateBuilder(args);
    /// builder.Services.AddProsodyClient(
    ///     builder.Configuration.GetSection("Prosody"),
    ///     client => client.WithMock(builder.Environment.IsDevelopment()));
    /// </code>
    /// </example>
    public static IServiceCollection AddProsodyClient(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<ProsodyClientBuilder>? configure
    )
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var options = configuration.Get<ClientOptions>() ?? new ClientOptions();

        services.AddSingleton(_ =>
        {
            var builder = Prosody.CreateClient().WithOptions(options);
            configure?.Invoke(builder);
            return builder.Build();
        });

        return services;
    }

    /// <summary>
    /// Adds a <see cref="ProsodyClient"/> to the service collection using builder configuration.
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    /// <param name="configure">An action to configure the client builder.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <remarks>
    /// <para>
    /// The client is registered as a singleton because it manages Kafka connections and internal state
    /// that should be shared across the application.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// var builder = WebApplication.CreateBuilder(args);
    /// builder.Services.AddProsodyClient(client => client
    ///     .WithBootstrapServers("localhost:9092")
    ///     .WithGroupId("my-app")
    ///     .WithSubscribedTopics("orders")
    ///     .WithMock(builder.Environment.IsDevelopment()));
    /// </code>
    /// </example>
    public static IServiceCollection AddProsodyClient(
        this IServiceCollection services,
        Action<ProsodyClientBuilder> configure
    )
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        services.AddSingleton(_ =>
        {
            var builder = Prosody.CreateClient();
            configure(builder);
            return builder.Build();
        });

        return services;
    }

    private sealed class ProsodyLoggingHostedService : IHostedService
    {
        private readonly ILoggerFactory _loggerFactory;

        public ProsodyLoggingHostedService(ILoggerFactory loggerFactory)
        {
            _loggerFactory = loggerFactory;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            ProsodyLogging.Configure(_loggerFactory);
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            ProsodyLogging.Configure(null);
            return Task.CompletedTask;
        }
    }
}
