using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Prosody.Configuration;
using Prosody.Logging;

namespace Prosody.Extensions;

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
    /// Adds a <see cref="ProsodyClient"/> to the service collection using configuration
    /// bound from the <c>Prosody</c> section of the application's <see cref="Microsoft.Extensions.Configuration.IConfiguration"/>.
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    /// <param name="configure">An optional action to further configure the client options after binding configuration.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <remarks>
    /// <para>
    /// Options are bound from the <c>Prosody</c> configuration section using the standard
    /// <see cref="IOptions{TOptions}"/> pipeline. The <paramref name="configure"/> action
    /// is applied via <see cref="OptionsServiceCollectionExtensions.PostConfigure{TOptions}(IServiceCollection, Action{TOptions})"/>
    /// after configuration binding.
    /// </para>
    /// <para>
    /// Validation runs at startup via <see cref="OptionsBuilderExtensions.ValidateOnStart{TOptions}(OptionsBuilder{TOptions})"/>.
    /// Invalid configuration throws <see cref="OptionsValidationException"/>.
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
    /// builder.Services.AddProsodyClient();
    ///
    /// // Or with programmatic overrides:
    /// builder.Services.AddProsodyClient(options =&gt; options.Mock = true);
    /// </code>
    /// </example>
    public static IServiceCollection AddProsodyClient(
        this IServiceCollection services,
        Action<ClientOptions>? configure = null
    ) => services.AddProsodyClient("Prosody", configure);

    /// <summary>
    /// Adds a <see cref="ProsodyClient"/> to the service collection using configuration
    /// bound from the specified configuration section path.
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    /// <param name="configSectionPath">The configuration section path to bind from (e.g. <c>"MyApp:Kafka"</c>).</param>
    /// <param name="configure">An optional action to further configure the client options after binding configuration.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <remarks>
    /// <para>
    /// Options are bound from the specified configuration section using the standard
    /// <see cref="IOptions{TOptions}"/> pipeline. The <paramref name="configure"/> action
    /// is applied via <see cref="OptionsServiceCollectionExtensions.PostConfigure{TOptions}(IServiceCollection, Action{TOptions})"/>
    /// after configuration binding.
    /// </para>
    /// <para>
    /// Validation runs at startup via <see cref="OptionsBuilderExtensions.ValidateOnStart{TOptions}(OptionsBuilder{TOptions})"/>.
    /// Invalid configuration throws <see cref="OptionsValidationException"/>.
    /// </para>
    /// <para>
    /// The client is registered as a singleton because it manages Kafka connections and internal state
    /// that should be shared across the application.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// var builder = WebApplication.CreateBuilder(args);
    /// builder.Services.AddProsodyClient("MyApp:Kafka", options =&gt; options.Mock = true);
    /// </code>
    /// </example>
    public static IServiceCollection AddProsodyClient(
        this IServiceCollection services,
        string configSectionPath,
        Action<ClientOptions>? configure = null
    )
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configSectionPath);

        var builder = services.AddOptions<ClientOptions>().BindConfiguration(configSectionPath);

        if (configure is not null)
        {
            builder.PostConfigure(configure);
        }

        builder.ValidateOnStart();

        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<ClientOptions>, ClientOptionsValidator>()
        );
        services.TryAddSingleton(sp =>
        {
            var options = sp.GetRequiredService<IOptions<ClientOptions>>().Value.Clone();
            return ProsodyClient.FromValidatedOptions(options);
        });

        return services;
    }

    private sealed class ProsodyLoggingHostedService(ILoggerFactory loggerFactory) : IHostedService
    {
        private readonly ILoggerFactory _loggerFactory = loggerFactory;

        public Task StartAsync(CancellationToken cancellationToken)
        {
            ProsodyLogging.Configure(_loggerFactory);
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            ProsodyLogging.Clear();
            return Task.CompletedTask;
        }
    }
}
