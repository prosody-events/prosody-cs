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
