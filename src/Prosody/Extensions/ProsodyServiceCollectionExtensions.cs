using System.Diagnostics.CodeAnalysis;
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
    /// This method registers a hosted service that configures Prosody logging in the host's
    /// starting phase, before any hosted service starts, and cleans up when the host stops.
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
    /// The service registers one <see cref="ProsodyClient"/>. Construction does no I/O. The first operation connects, or set
    /// <see cref="ClientOptions.ConnectOnStart"/> to connect when the host starts. A hosted
    /// lifecycle service disposes the client inside the host's stop deadline.
    /// </para>
    /// <para>
    /// Repeated calls are safe. The first call binds and registers; later calls with the same
    /// section only add their <paramref name="configure"/> action. A later call with a different
    /// section throws.
    /// </para>
    /// <para>
    /// Keyed-state collections are programmatic (not configuration-bindable): set
    /// <see cref="ClientOptions.StateCollections"/> in the <paramref name="configure"/> callback, for
    /// example <c>options.StateCollections = [cart, totals]</c>.
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
    [RequiresUnreferencedCode(Trimming.OptionsBinding)]
    [RequiresDynamicCode(Trimming.OptionsBinding)]
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
    /// that should be shared across the application. See the parameterless overload for the
    /// connect and repeated-call behavior.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// var builder = WebApplication.CreateBuilder(args);
    /// builder.Services.AddProsodyClient("MyApp:Kafka", options =&gt; options.Mock = true);
    /// </code>
    /// </example>
    [RequiresUnreferencedCode(Trimming.OptionsBinding)]
    [RequiresDynamicCode(Trimming.OptionsBinding)]
    public static IServiceCollection AddProsodyClient(
        this IServiceCollection services,
        string configSectionPath,
        Action<ClientOptions>? configure = null
    )
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configSectionPath);

        // Reject a different section before this call adds any services.
        var existing = services.FirstOrDefault(d => d.ServiceType == typeof(Registration));
        if (
            existing?.ImplementationInstance is Registration registration
            && !string.Equals(registration.ConfigSectionPath, configSectionPath, StringComparison.Ordinal)
        )
        {
            throw new InvalidOperationException(
                $"AddProsodyClient was already called with configuration section '{registration.ConfigSectionPath}'. One application registers one Prosody client."
            );
        }

        if (configure is not null)
        {
            services.PostConfigure(configure);
        }

        // Bind only once. A second binding duplicates array entries.
        if (existing is not null)
        {
            return services;
        }

        services.AddSingleton(new Registration(configSectionPath));
        services.AddOptions<ClientOptions>().BindConfiguration(configSectionPath).ValidateOnStart();
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<ClientOptions>, ClientOptionsValidator>()
        );

        services.TryAddSingleton(sp => new ProsodyClient(
            sp.GetRequiredService<IOptions<ClientOptions>>().Value,
            sp.GetService<ILogger<ProsodyClient>>()
        ));
#pragma warning disable CS0618 // The adapter keeps existing GetRequiredService<ProsodyClientProvider>() calls resolving.
        services.TryAddSingleton(sp => new ProsodyClientProvider(sp.GetRequiredService<ProsodyClient>()));
#pragma warning restore CS0618
        services.AddHostedService<ProsodyClientLifecycle>();

        return services;
    }

    /// <summary>Marks that <see cref="AddProsodyClient(IServiceCollection, string, Action{ClientOptions}?)"/> already ran, and with which section.</summary>
    private sealed record Registration(string ConfigSectionPath);

    /// <summary>Configures logging in the starting phase, so an eager client connect in the start phase logs.</summary>
    private sealed class ProsodyLoggingHostedService(ILoggerFactory loggerFactory)
        : IHostedLifecycleService,
            IDisposable
    {
        // Only this service can clear the configuration it acquired. Stop and disposal share the release.
        private bool _configured;

        public Task StartingAsync(CancellationToken cancellationToken)
        {
            ProsodyLogging.Configure(loggerFactory);
            _configured = true;
            return Task.CompletedTask;
        }

        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken)
        {
            Dispose();
            return Task.CompletedTask;
        }

        public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public void Dispose()
        {
            if (_configured)
            {
                _configured = false;
                ProsodyLogging.Clear();
            }
        }
    }
}
