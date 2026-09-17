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
/// is <c>true</c>. A cancelled or failed connection aborts host startup.
/// With sequential startup, hosted services registered after the client start after the connection completes.
/// With <c>HostOptions.ServicesStartConcurrently = true</c>, those services can start while the connection is pending.
/// Client operations still await the shared connection.
/// </para>
/// <para>
/// The connection runs in the start phase. The logging hosted service configures
/// <see cref="ProsodyLogging"/> in the starting phase, before the connection, regardless of registration order.
/// </para>
/// <para>
/// <see cref="StoppedAsync"/> disposes the client after every hosted service has stopped, inside
/// the host's stop deadline. If the deadline fires first, the wait is abandoned and logged.
/// The disposal still completes in the background.
/// </para>
/// <para>
/// The service logs through the injected logger, not <see cref="ProsodyLogging"/>. The logging
/// hosted service clears <see cref="ProsodyLogging"/> in its stop phase, which runs before
/// <see cref="StoppedAsync"/>.
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
        : this(client.ConnectAsync, client.DisposeAsync, options.Value.ConnectOnStart == true, logger) { }

    public Task StartingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StartAsync(CancellationToken cancellationToken) =>
        connectOnStart ? connect(cancellationToken) : Task.CompletedTask;

    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public async Task StoppedAsync(CancellationToken cancellationToken)
    {
        // The client closes itself before this call returns and releases the native handle on
        // the pool. Calling it inline here means a deadline that fires first still leaves nothing
        // for container disposal to claim and wait on.
        var disposal = dispose().AsTask();
        await AwaitDisposalAsync(disposal, disposal.WaitAsync(cancellationToken), cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Handles the disposal task and its deadline wait. A cancelled wait is abandoned even if disposal has since completed.
    /// </summary>
    internal async Task AwaitDisposalAsync(Task disposal, Task wait, CancellationToken cancellationToken)
    {
        try
        {
            await wait.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            LogHelper.LogDisposalAbandoned(logger);
            _ = disposal.ContinueWith(
                static (completed, state) =>
                    LogHelper.LogShutdownFailed((ILogger)state!, completed.Exception!.GetBaseException()),
                logger,
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default
            );
        }
    }
}
