using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.Logging;
using Prosody.Configuration;
using Prosody.Errors;
using Prosody.Infrastructure;
using Prosody.Logging;
using Prosody.State;
#if NET9_0_OR_GREATER
using ClientLock = System.Threading.Lock;
#else
using ClientLock = System.Object;
#endif

namespace Prosody;

// This file owns client construction, the shared connection, JSON options, shutdown, and disposal.
// The other ProsodyClient.*.cs files own the consumer, send, request, and published-state members.

/// <summary>
/// Main client for interacting with the Prosody messaging system.
/// </summary>
/// <remarks>
/// <para>
/// Construction opens no connection and makes no native call. The first operation, or <see cref="ConnectAsync"/>, starts the
/// connect. All operations share one connect. A cancelled caller stops only its own wait, and
/// the connect continues for later callers. A failed connect is not kept, so the next operation
/// tries again.
/// </para>
/// <para>
/// <see cref="DisposeAsync"/> never waits on a pending connect. It releases the result when the
/// connect completes.
/// </para>
/// </remarks>
public sealed partial class ProsodyClient : IDisposable, IAsyncDisposable
{
    /// <summary>The trim warning for an API that can install the reflection JSON resolver.</summary>
    internal const string DefaultResolverTrimWarning =
        "Auto-installs DefaultJsonTypeInfoResolver when no TypeInfoResolver is set via ConfigureJsonOptions. Configure a source-generated JsonSerializerContext to use trim-safe serialization.";

    /// <summary>The AOT warning for an API that can install the reflection JSON resolver.</summary>
    internal const string DefaultResolverAotWarning =
        "Auto-installs DefaultJsonTypeInfoResolver when no TypeInfoResolver is set via ConfigureJsonOptions. Configure a source-generated JsonSerializerContext to avoid runtime code generation.";

    private readonly ClientLock _gate = new();
    private readonly Func<Task<Native.ProsodyClient>> _connect;
    private readonly ILogger _logger;
    private readonly IReadOnlySet<StateDefinition> _stateDefinitions;
    private readonly Lazy<Task> _shutdown;

    // Invariant: _native is null, a pending build, or a completed build that every caller shares.
    // A successful build is never replaced. SharedBuild evicts a failed build, so the next
    // operation retries. _closed and _claimed change only from false to true. A closed client
    // starts no build and hands out no native client. _claimed gives one disposer the build to
    // release. _gate guards every write. Reads outside the lock use Volatile.Read.
    private Task<Native.ProsodyClient>? _native;
    private bool _closed;
    private bool _claimed;

    internal JsonSerializerOptions JsonOptions { get; }

    /// <summary>Creates an unconnected client from validated options.</summary>
    /// <param name="validated">The options. The client keeps a copy.</param>
    /// <param name="logger">Logs disposal failures. The default comes from <see cref="ProsodyLogging"/>.</param>
    /// <param name="connect">Builds the native client. Tests replace it to control the build.</param>
    /// <exception cref="InvalidOperationException">No source system or consumer group id is configured.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A duration option is negative.</exception>
    [RequiresUnreferencedCode(DefaultResolverTrimWarning)]
    [RequiresDynamicCode(DefaultResolverAotWarning)]
    internal ProsodyClient(
        ClientOptions validated,
        ILogger? logger = null,
        Func<Task<Native.ProsodyClient>>? connect = null
    )
    {
        var options = validated.Clone();
        var nativeOptions = options.ToNative();
        JsonOptions = BuildJsonOptions(options);
        _stateDefinitions = RegisteredStateDefinitions(options);
        SourceSystem =
            options.ResolveSourceSystem()
            ?? throw new InvalidOperationException("No source system or consumer group id is configured.");
        _connect = connect ?? (() => Native.ProsodyClient.ProsodyClientAsync(nativeOptions));
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
    [RequiresUnreferencedCode(DefaultResolverTrimWarning)]
    [RequiresDynamicCode(DefaultResolverAotWarning)]
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
        var native = await NativeErrors.RunAsync(_connect).ConfigureAwait(false);

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
    /// <remarks>Use the token to limit the connect wait, for example in a health check or a worker.</remarks>
    /// <exception cref="OperationCanceledException">The caller's token was cancelled. The connect continues.</exception>
    /// <exception cref="ObjectDisposedException">The client is disposed or shut down.</exception>
    public async Task ConnectAsync(CancellationToken cancellationToken = default) =>
        await NativeAsync(cancellationToken).ConfigureAwait(false);

    /// <summary>Returns the connected native client. Starts the shared build when none is cached.</summary>
    /// <exception cref="OperationCanceledException">The caller's token was cancelled. The build continues.</exception>
    /// <exception cref="ObjectDisposedException">The client is disposed or shut down.</exception>
    private async ValueTask<Native.ProsodyClient> NativeAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // A connected client takes no lock.
        var build = Volatile.Read(ref _native) is { IsCompletedSuccessfully: true } ready ? ready : SharedBuild();
        var native = await build.WaitAsync(cancellationToken).ConfigureAwait(false);

        // A wait can outlive ShutdownAsync or DisposeAsync.
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _closed), this);
        return native;
    }

    private Task<Native.ProsodyClient> SharedBuild()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_closed, this);

            // A failed build leaves the cache here, so this call retries it. A cancelled build
            // counts as failed. Reading Exception marks a fault observed.
            if (_native is { IsCompleted: true, IsCompletedSuccessfully: false } failed)
            {
                _ = failed.Exception;
                _native = null;
            }
            return _native ??= BuildAsync();
        }
    }

    private static HashSet<StateDefinition> RegisteredStateDefinitions(ClientOptions options) =>
        [.. options.StateCollections ?? []];

    [RequiresUnreferencedCode(DefaultResolverTrimWarning)]
    [RequiresDynamicCode(DefaultResolverAotWarning)]
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
    /// <remarks>
    /// Resolved once at construction. Each connect proves the native client resolved the same
    /// value, so <c>PROSODY_SOURCE_SYSTEM</c> and <c>PROSODY_GROUP_ID</c> must not change while
    /// the client lives.
    /// </remarks>
    public string SourceSystem { get; }

    /// <summary>
    /// Shuts down all client services and rejects new operations.
    /// Concurrent and repeated calls await the same shutdown operation.
    /// </summary>
    /// <remarks>
    /// The client closes first, so a later operation throws <see cref="ObjectDisposedException"/>
    /// and starts no connect. This method waits for a pending connect, then shuts the native
    /// client down. <see cref="DisposeAsync"/> still releases the native handle.
    /// </remarks>
    public Task ShutdownAsync() => _shutdown.Value;

    private async Task ShutdownCoreAsync()
    {
        Task<Native.ProsodyClient>? build;
        lock (_gate)
        {
            _closed = true;
            build = _native;
        }

        if (build is not null && await SettleAsync(build).ConfigureAwait(false) is { } native)
        {
            await NativeErrors.RunAsync(native.Shutdown).ConfigureAwait(false);
        }
    }

    /// <summary>Awaits a build without throwing. Returns the client on success, otherwise <c>null</c>.</summary>
    private static async Task<Native.ProsodyClient?> SettleAsync(Task<Native.ProsodyClient> build)
    {
        // SuppressThrowing also marks a fault observed. Disposal can be the only code that sees it.
        await ((Task)build).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        return build.IsCompletedSuccessfully ? await build.ConfigureAwait(false) : null;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Closes the client before this call returns. When the connect has completed, the returned
    /// task shuts the native client down and releases it. This method never waits on a pending
    /// connect: the release runs when the connect completes. A shutdown error is logged, not thrown.
    /// </remarks>
    public ValueTask DisposeAsync()
    {
        if (!TryClose(out var build))
        {
            return ValueTask.CompletedTask;
        }

        var release = ReleaseAsync(build);
        return build.IsCompleted ? new ValueTask(release) : ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    /// <remarks>Starts shutdown and release without waiting for them. Prefer <see cref="DisposeAsync"/>.</remarks>
    public void Dispose()
    {
        if (TryClose(out var build))
        {
            _ = ReleaseAsync(build);
        }
    }

    /// <summary>Closes the client and claims the shared build for release. Returns it on the first claim only.</summary>
    private bool TryClose([NotNullWhen(true)] out Task<Native.ProsodyClient>? build)
    {
        lock (_gate)
        {
            _closed = true;
            build = _claimed ? null : _native;
            _claimed = true;
            return build is not null;
        }
    }

    /// <summary>Shuts down and releases the native client of <paramref name="build"/>. Never throws.</summary>
    /// <remarks>Disposal often has no caller left to observe a fault, so every error is logged here.</remarks>
    private async Task ReleaseAsync(Task<Native.ProsodyClient> build)
    {
        if (await SettleAsync(build).ConfigureAwait(false) is not { } native)
        {
            return;
        }

        try
        {
            await ShutdownAsync().ConfigureAwait(false);
        }
#pragma warning disable CA1031 // Release must reach native.Dispose() whatever shutdown throws.
        catch (Exception error)
#pragma warning restore CA1031
        {
            LogHelper.LogShutdownFailed(_logger, error);
        }
        finally
        {
            // Flush this client's final telemetry before native teardown.
            // Telemetry is process-global, so sibling clients can still use it.
            try
            {
                ProsodyLogging.FlushTelemetry();
            }
            catch (ProsodyException)
            {
                // Telemetry flush is best-effort during disposal.
            }

            native.Dispose();
        }
    }
}
