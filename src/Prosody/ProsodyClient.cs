using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Prosody.Configuration;
using Prosody.Errors;
using Prosody.Infrastructure;
using Prosody.Logging;
using Prosody.State;

namespace Prosody;

// This file owns client construction, JSON options, shutdown, and disposal.
// The other ProsodyClient.*.cs files own the consumer, send, request, and published-state members.

/// <summary>
/// Main client for interacting with the Prosody messaging system.
/// </summary>
public sealed partial class ProsodyClient : IDisposable, IAsyncDisposable
{
    /// <summary>The trim warning for an API that can install the reflection JSON resolver.</summary>
    internal const string DefaultResolverTrimWarning =
        "Auto-installs DefaultJsonTypeInfoResolver when no TypeInfoResolver is set via ConfigureJsonOptions. Configure a source-generated JsonSerializerContext to use trim-safe serialization.";

    /// <summary>The AOT warning for an API that can install the reflection JSON resolver.</summary>
    internal const string DefaultResolverAotWarning =
        "Auto-installs DefaultJsonTypeInfoResolver when no TypeInfoResolver is set via ConfigureJsonOptions. Configure a source-generated JsonSerializerContext to avoid runtime code generation.";

    private readonly Native.ProsodyClient _native;
    private readonly IReadOnlySet<StateDefinition> _stateDefinitions;
    private readonly Lazy<Task> _shutdown;

    internal JsonSerializerOptions JsonOptions { get; }

    // The native client is the last parameter, so a caller evaluates it last.
    // If the JSON options fail to build, the caller opens no connection.
    private ProsodyClient(
        JsonSerializerOptions jsonOptions,
        IReadOnlySet<StateDefinition> stateDefinitions,
        Native.ProsodyClient native
    )
    {
        _native = native;
        JsonOptions = jsonOptions;
        _stateDefinitions = stateDefinitions;
        _shutdown = new(ShutdownCoreAsync, LazyThreadSafetyMode.ExecutionAndPublication);
        SourceSystem = native.SourceSystem();
    }

    /// <summary>
    /// Creates a new Prosody client with the given options.
    /// </summary>
    /// <param name="options">Configuration options for the client.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="options"/> is null.</exception>
    /// <exception cref="InvalidOperationException">Thrown when <paramref name="options"/> fails validation.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when a duration option is negative.</exception>
    /// <exception cref="ProsodyException">Thrown when Prosody cannot connect to Kafka or Cassandra.</exception>
    /// <remarks>
    /// When no <c>TypeInfoResolver</c> is set via <see cref="ClientOptions.ConfigureJsonOptions"/>,
    /// this constructor auto-installs <c>DefaultJsonTypeInfoResolver</c>, which uses reflection metadata.
    /// To avoid this, set <c>TypeInfoResolver</c> to a source-generated <c>JsonSerializerContext</c>
    /// in the <see cref="ClientOptions.ConfigureJsonOptions"/> callback.
    /// </remarks>
    [RequiresUnreferencedCode(DefaultResolverTrimWarning)]
    [RequiresDynamicCode(DefaultResolverAotWarning)]
    public static async Task<ProsodyClient> CreateAsync(ClientOptions options) =>
        await FromValidatedOptionsAsync(Validated(options)).ConfigureAwait(false);

    /// <summary>
    /// Creates a new Prosody client with the given options and blocks the calling thread until it is ready.
    /// </summary>
    /// <param name="options">Configuration options for the client.</param>
    /// <inheritdoc cref="CreateAsync(ClientOptions)" path="/exception|/remarks"/>
    [RequiresUnreferencedCode(DefaultResolverTrimWarning)]
    [RequiresDynamicCode(DefaultResolverAotWarning)]
    public ProsodyClient(ClientOptions options)
        // Arguments evaluate left to right, so the options are valid before the other arguments read them.
        : this(
            BuildJsonOptions(Validated(options)),
            RegisteredStateDefinitions(options),
            NativeErrors.Run(() => Native.ProsodyClient.NewBlocking(options.ToNative()))
        ) { }

    private static ClientOptions Validated(ClientOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        return options;
    }

    /// <summary>
    /// Creates a new ProsodyClient from pre-validated options, skipping redundant validation.
    /// </summary>
    [RequiresUnreferencedCode(DefaultResolverTrimWarning)]
    [RequiresDynamicCode(DefaultResolverAotWarning)]
    internal static async Task<ProsodyClient> FromValidatedOptionsAsync(ClientOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var native = options.ToNative();
        return new ProsodyClient(
            BuildJsonOptions(options),
            RegisteredStateDefinitions(options),
            await NativeErrors.RunAsync(() => Native.ProsodyClient.ProsodyClientAsync(native)).ConfigureAwait(false)
        );
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
    public string SourceSystem { get; }

    /// <summary>
    /// Shuts down all client services.
    /// Concurrent and repeated calls await the same shutdown operation.
    /// </summary>
    /// <exception cref="ProsodyException">A client service failed to shut down.</exception>
    public Task ShutdownAsync() => _shutdown.Value;

    private Task ShutdownCoreAsync() => NativeErrors.RunAsync(_native.Shutdown);

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        try
        {
            await ShutdownAsync().ConfigureAwait(false);
        }
        catch (ProsodyException error)
        {
            LogHelper.LogShutdownFailed(ProsodyLogging.CreateLogger(nameof(ProsodyClient)), error);
        }
        catch (ObjectDisposedException)
        {
            // A prior synchronous disposal already released the native client.
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

            _native.Dispose();
        }
    }

    /// <inheritdoc/>
    public void Dispose() => _native.Dispose();
}
