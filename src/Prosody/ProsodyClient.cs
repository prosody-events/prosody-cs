using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.Logging;
using Prosody.Configuration;
using Prosody.Errors;
using Prosody.Infrastructure;
using Prosody.Logging;
using Prosody.Messaging;
using Prosody.State;
#if NET9_0_OR_GREATER
using ClientLock = System.Threading.Lock;
#else
using ClientLock = System.Object;
#endif

namespace Prosody;

/// <summary>
/// Main client for interacting with the Prosody messaging system.
/// </summary>
/// <remarks>
/// <para>
/// Construction is synchronous and does no I/O. The first operation, or
/// <see cref="ConnectAsync"/>, starts the native build. A caller's cancellation abandons that
/// caller's wait only: the build continues, stays cached, and serves later callers or is torn
/// down by <see cref="DisposeAsync"/>. The native build never starts a second time because a
/// caller cancelled. A failed build is not retained; the next operation retries.
/// </para>
/// <para>
/// <see cref="DisposeAsync"/> never waits on a pending build. It disposes whatever the build
/// produces once the build settles.
/// </para>
/// </remarks>
public sealed partial class ProsodyClient : IDisposable, IAsyncDisposable
{
    /// <summary>The crate's default handler-drain budget, used when <see cref="ClientOptions.ShutdownTimeout"/> is unset.</summary>
    private static readonly TimeSpan DefaultShutdownTimeout = TimeSpan.FromSeconds(30);

    /// <summary>Added to the shutdown budget so the crate's own terminate fires before disposal gives up.</summary>
    private static readonly TimeSpan ShutdownMargin = TimeSpan.FromSeconds(5);

    private readonly ClientLock _gate = new();
    private readonly Func<Task<Native.ProsodyClient>> _connect;
    private readonly Func<Native.ProsodyClient, Task> _shutdownNative;

    // Container disposal has no host deadline after startup fails. This budget bounds native shutdown.
    private readonly TimeSpan _shutdownBudget;
    private readonly ILogger _logger;
    private readonly IReadOnlySet<StateDefinition> _stateDefinitions;
    private readonly Lazy<Task> _shutdown;

    // Invariant: _native is null or the one build every caller shares. NativeAsync creates it.
    // Only completed unsuccessful builds leave the cache. _closed and _claimed change only
    // from false to true. A closed client starts no build. _claimed gives one disposer the
    // build for release. _gate protects these fields.
    private Task<Native.ProsodyClient>? _native;
    private bool _closed;
    private bool _claimed;

    internal JsonSerializerOptions JsonOptions { get; }

    /// <summary>Creates an unconnected client from validated options.</summary>
    [RequiresUnreferencedCode(Trimming.JsonResolver)]
    [RequiresDynamicCode(Trimming.JsonResolver)]
    internal ProsodyClient(ClientOptions validated, ILogger? logger = null)
        : this(validated, connect: null, shutdownNative: null, logger) { }

    /// <summary>
    /// Creates an unconnected client. Tests pass <paramref name="connect"/> to drive the native
    /// build and <paramref name="shutdownNative"/> to drive the native shutdown.
    /// </summary>
    [RequiresUnreferencedCode(Trimming.JsonResolver)]
    [RequiresDynamicCode(Trimming.JsonResolver)]
    internal ProsodyClient(
        ClientOptions validated,
        Func<Task<Native.ProsodyClient>>? connect,
        Func<Native.ProsodyClient, Task>? shutdownNative = null,
        ILogger? logger = null
    )
    {
        var options = validated.Clone();
        JsonOptions = BuildJsonOptions(options);
        _stateDefinitions = RegisteredStateDefinitions(options);
        SourceSystem =
            options.ResolveSourceSystem()
            ?? throw new InvalidOperationException("No source system or consumer group id is configured.");
        _connect = connect ?? (() => Native.ProsodyClient.ProsodyClientAsync(options.ToNative()));
        _shutdownNative = shutdownNative ?? (native => native.Shutdown());
        _shutdownBudget = (options.ResolveShutdownTimeout() ?? DefaultShutdownTimeout) + ShutdownMargin;
        // Keep the logger after the logging service clears its factory during host stop.
        _logger = logger ?? ProsodyLogging.CreateLogger(nameof(ProsodyClient));
        _shutdown = new(ShutdownCoreAsync, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    /// <summary>
    /// Creates a Prosody client with the given options and connects it.
    /// </summary>
    /// <param name="options">Configuration options for the client.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="options"/> is null.</exception>
    /// <exception cref="InvalidOperationException">Thrown when <paramref name="options"/> fails validation.</exception>
    /// <remarks>
    /// When no <c>TypeInfoResolver</c> is set via <see cref="ClientOptions.ConfigureJsonOptions"/>,
    /// this method auto-installs <c>DefaultJsonTypeInfoResolver</c>, which uses reflection metadata.
    /// To avoid this, set <c>TypeInfoResolver</c> to a source-generated <c>JsonSerializerContext</c>
    /// in the <see cref="ClientOptions.ConfigureJsonOptions"/> callback.
    /// </remarks>
    [RequiresUnreferencedCode(Trimming.JsonResolver)]
    [RequiresDynamicCode(Trimming.JsonResolver)]
    public static async Task<ProsodyClient> CreateAsync(ClientOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        var client = new ProsodyClient(options);
        await client.ConnectAsync().ConfigureAwait(false);
        return client;
    }

    private async Task<Native.ProsodyClient> BuildAsync()
    {
        var native = await _connect().ConfigureAwait(false);

        // SourceSystem was resolved before connect with the crate's precedence. Prove it matched.
        var actual = native.SourceSystem();
        if (!string.Equals(actual, SourceSystem, StringComparison.Ordinal))
        {
            native.Dispose();
            throw new InvalidOperationException(
                $"Native source system '{actual}' differs from the resolved '{SourceSystem}'."
            );
        }
        return native;
    }

    /// <summary>Connects now instead of on first use. Safe to call more than once.</summary>
    /// <exception cref="OperationCanceledException">The caller's token was cancelled. The build continues.</exception>
    /// <exception cref="ObjectDisposedException">The client is disposed or shut down.</exception>
    public async Task ConnectAsync(CancellationToken cancellationToken = default) =>
        await NativeAsync(cancellationToken).ConfigureAwait(false);

    private async ValueTask<Native.ProsodyClient> NativeAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        Task<Native.ProsodyClient> pending;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_closed, this);
            // A build that failed after every waiter had cancelled is still cached here.
            // Evict it so this call retries instead of replaying the old failure.
            // IsFaulted alone is not enough: a build that ended Canceled would stay forever.
            if (_native is { IsCompleted: true, IsCompletedSuccessfully: false } failed)
            {
                // Reading Exception marks the fault observed. A cancelled WaitAsync waiter removes
                // its continuation from the build, so nothing else observes it once evicted.
                _ = failed.Exception;
                _native = null;
            }
            pending = _native ??= BuildAsync();
        }

        return await AwaitNativeAsync(pending, pending.WaitAsync(cancellationToken)).ConfigureAwait(false);
    }

    /// <summary>Checks the build after its caller's wait completes. A cancelled wait can precede a build fault.</summary>
    internal async ValueTask<Native.ProsodyClient> AwaitNativeAsync(
        Task<Native.ProsodyClient> pending,
        Task<Native.ProsodyClient> wait
    )
    {
        try
        {
            var native = await wait.ConfigureAwait(false);
            // A waiter that outlived ShutdownAsync or DisposeAsync must not start work on a
            // handle that is shut down or about to be released.
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_closed, this);
            }
            return native;
        }
        catch when (pending.IsCompleted && !pending.IsCompletedSuccessfully)
        {
            // Observe a build fault even when the caller's wait completed through cancellation.
            _ = pending.Exception;

            // Only a failed build resets the cache. A caller cancelling must not
            // trigger a second native build while the first is still connecting.
            lock (_gate)
            {
                if (ReferenceEquals(_native, pending))
                {
                    _native = null;
                }
            }
            throw;
        }
    }

    private static HashSet<StateDefinition> RegisteredStateDefinitions(ClientOptions options) =>
        new HashSet<StateDefinition>(options.StateCollections ?? [], ReferenceEqualityComparer.Instance);

    [RequiresUnreferencedCode(Trimming.JsonResolver)]
    [RequiresDynamicCode(Trimming.JsonResolver)]
    private static JsonSerializerOptions BuildJsonOptions(ClientOptions options)
    {
        var opts = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };
        opts.Converters.Add(new JsonStringEnumConverter());
        options.ConfigureJsonOptions?.Invoke(opts);
        opts.TypeInfoResolver ??= new DefaultJsonTypeInfoResolver();
        opts.MakeReadOnly();
        return opts;
    }

    /// <summary>
    /// Gets the source system identifier configured for this client.
    /// </summary>
    public string SourceSystem { get; }

    /// <summary>
    /// Gets the current consumer state.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the consumer configuration failed during build, with the full error message.
    /// </exception>
    public Task<ConsumerState> GetConsumerStateAsync() => GetConsumerStateAsync(CancellationToken.None);

    /// <inheritdoc cref="GetConsumerStateAsync()"/>
    /// <param name="cancellationToken">Bounds the wait for the connect only. The query itself is not cancellable.</param>
    public async Task<ConsumerState> GetConsumerStateAsync(CancellationToken cancellationToken)
    {
        var native = await NativeAsync(cancellationToken).ConfigureAwait(false);
        Native.ConsumerState state = await native.ConsumerState().ConfigureAwait(false);
        return state switch
        {
            Native.ConsumerState.Shutdown => ConsumerState.Shutdown,
            Native.ConsumerState.Unconfigured => ConsumerState.Unconfigured,
            Native.ConsumerState.Configured => ConsumerState.Configured,
            Native.ConsumerState.Running => ConsumerState.Running,
            Native.ConsumerState.ConfigurationFailed failed => throw new InvalidOperationException(
                $"Consumer configuration failed: {failed.Message}"
            ),
            _ => throw new InvalidOperationException("Unknown consumer state"),
        };
    }

    /// <summary>
    /// Gets the number of partitions currently assigned to this consumer.
    /// </summary>
    public Task<uint> AssignedPartitionCountAsync() => AssignedPartitionCountAsync(CancellationToken.None);

    /// <inheritdoc cref="AssignedPartitionCountAsync()"/>
    /// <param name="cancellationToken">Bounds the wait for the connect only. The query itself is not cancellable.</param>
    public async Task<uint> AssignedPartitionCountAsync(CancellationToken cancellationToken)
    {
        var native = await NativeAsync(cancellationToken).ConfigureAwait(false);
        return await native.AssignedPartitionCount().ConfigureAwait(false);
    }

    /// <summary>
    /// Gets a value indicating whether the consumer is currently stalled.
    /// </summary>
    public Task<bool> IsStalledAsync() => IsStalledAsync(CancellationToken.None);

    /// <inheritdoc cref="IsStalledAsync()"/>
    /// <param name="cancellationToken">Bounds the wait for the connect only. The query itself is not cancellable.</param>
    public async Task<bool> IsStalledAsync(CancellationToken cancellationToken)
    {
        var native = await NativeAsync(cancellationToken).ConfigureAwait(false);
        return await native.IsStalled().ConfigureAwait(false);
    }

    /// <summary>
    /// Shuts down all client services and rejects new operations.
    /// Concurrent and repeated calls await the same shutdown operation.
    /// </summary>
    /// <remarks>
    /// The client is closed first, so no later operation starts a build or reaches the native
    /// client; each throws <see cref="ObjectDisposedException"/>. A pending build is awaited,
    /// then shut down. <see cref="DisposeAsync"/> still releases the native handle.
    /// </remarks>
    public Task ShutdownAsync() => _shutdown.Value;

    private async Task ShutdownCoreAsync()
    {
        Task<Native.ProsodyClient>? pending;
        lock (_gate)
        {
            _closed = true;
            pending = _native;
        }

        if (pending is not null && await SettleAsync(pending).ConfigureAwait(false) is { } native)
        {
            await _shutdownNative(native).ConfigureAwait(false);
        }
    }

    /// <summary>Awaits a build without throwing. Returns the client on success, otherwise <c>null</c>.</summary>
    private static async Task<Native.ProsodyClient?> SettleAsync(Task<Native.ProsodyClient> pending)
    {
        await ((Task)pending).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        return pending.IsCompletedSuccessfully ? await pending.ConfigureAwait(false) : null;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Closes the client before this call returns. Releases the native handle on the thread pool.
    /// The returned task waits for release only if the build has already completed.
    /// Native shutdown uses the resolved <see cref="ClientOptions.ShutdownTimeout"/> plus a five-second margin.
    /// A late release fault is logged.
    /// </remarks>
    public ValueTask DisposeAsync()
    {
        if (!TryClose(out var pending))
        {
            return ValueTask.CompletedTask;
        }

        var release = Task.Run(() => DisposeNativeAsync(pending));
        if (pending.IsCompleted)
        {
            return new ValueTask(release);
        }

        LogWhenFaulted(release);
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    /// <remarks>Starts shutdown and release without blocking. Prefer <see cref="DisposeAsync"/>.</remarks>
    public void Dispose()
    {
        if (TryClose(out var pending))
        {
            LogWhenFaulted(Task.Run(() => DisposeNativeAsync(pending)));
        }
    }

    /// <summary>Closes the client and claims the shared build for release. Returns it on the first claim only.</summary>
    private bool TryClose([NotNullWhen(true)] out Task<Native.ProsodyClient>? pending)
    {
        lock (_gate)
        {
            _closed = true;
            pending = _claimed ? null : _native;
            _claimed = true;
            return pending is not null;
        }
    }

    private void LogWhenFaulted(Task release) =>
        _ = release.ContinueWith(
            static (disposal, logger) =>
                LogHelper.LogShutdownFailed((ILogger)logger!, disposal.Exception!.GetBaseException()),
            _logger,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default
        );

    private async Task DisposeNativeAsync(Task<Native.ProsodyClient> pending)
    {
        if (await SettleAsync(pending).ConfigureAwait(false) is not { } native)
        {
            return;
        }

        try
        {
            await ShutdownAsync().WaitAsync(_shutdownBudget).ConfigureAwait(false);
        }
        catch (Native.FfiException error)
        {
            LogHelper.LogShutdownFailed(_logger, error);
        }
        catch (TimeoutException)
        {
            LogHelper.LogNativeShutdownAbandoned(_logger, _shutdownBudget);
        }
        finally
        {
            // Flush this client's final telemetry before native teardown.
            // Telemetry is process-global, so sibling clients can still use it.
            try
            {
                ProsodyLogging.FlushTelemetry();
            }
            catch (Native.FfiException)
            {
                // Telemetry flush is best-effort during disposal.
            }

            native.Dispose();
        }
    }
}
