using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Prosody.Configuration;
using Prosody.Logging;

namespace Prosody.Extensions;

/// <summary>
/// Ties the shared <see cref="ProsodyClient"/> to the host lifecycle.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="StartAsync"/> connects the client when <see cref="ClientOptions.ConnectOnStart"/>
/// is <c>true</c>. A failed or cancelled connect stops host startup. The logging hosted service
/// configures <see cref="ProsodyLogging"/> in the starting phase, so this connect logs.
/// </para>
/// <para>
/// <see cref="StoppedAsync"/> disposes the client after every hosted service stops, inside the
/// host's stop deadline. If the deadline fires first, the service logs that it stopped the wait.
/// Disposal continues in the background and logs its own failures. This service logs through
/// the injected logger, because the logging hosted service clears <see cref="ProsodyLogging"/>
/// before this phase.
/// </para>
/// </remarks>
internal sealed class ProsodyClientLifecycle(
    Func<CancellationToken, Task> connect,
    Func<ValueTask> dispose,
    bool connectOnStart,
    ILogger logger
) : IHostedLifecycleService
{
    public ProsodyClientLifecycle(
        ProsodyClient client,
        IOptions<ClientOptions> options,
        ILogger<ProsodyClientLifecycle> logger
    )
        : this(client.ConnectAsync, client.DisposeAsync, options.Value.ConnectOnStart, logger) { }

    public Task StartingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StartAsync(CancellationToken cancellationToken) =>
        connectOnStart ? connect(cancellationToken) : Task.CompletedTask;

    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public async Task StoppedAsync(CancellationToken cancellationToken)
    {
        // The client closes before dispose() returns, so container disposal later finds nothing to wait on.
        try
        {
            await dispose().AsTask().WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            LogHelper.LogDisposalAbandoned(logger);
        }
    }
}
