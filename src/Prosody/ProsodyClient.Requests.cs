using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Prosody.Errors;
using Prosody.Infrastructure;
using Prosody.Messaging;

namespace Prosody;

// Request and response over the message bus: one outcome per subsystem.
public sealed partial class ProsodyClient
{
    /// <summary>Sends one request and returns one outcome per subsystem.</summary>
    /// <remarks>
    /// A missed deadline returns <see cref="TimeoutError"/> for that subsystem.
    /// A request-level failure throws instead of returning a partial dictionary.
    /// </remarks>
    /// <exception cref="ArgumentException">A subsystem name is invalid.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The timeout is negative.</exception>
    /// <exception cref="OperationCanceledException">The cancellation token was canceled.</exception>
    [RequiresUnreferencedCode(Trimming.JsonMetadata)]
    [RequiresDynamicCode(Trimming.JsonMetadata)]
    public Task<IReadOnlyDictionary<string, Outcome<TResponse>>> RequestAsync<TPayload, TResponse>(
        string topic,
        string key,
        TPayload payload,
        IReadOnlyList<string> subsystems,
        TimeSpan timeout,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(topic);
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(subsystems);
        cancellationToken.ThrowIfCancellationRequested();
        var payloadType = (JsonTypeInfo<TPayload>)JsonOptions.GetTypeInfo(typeof(TPayload));
        var responseType = (JsonTypeInfo<TResponse>)JsonOptions.GetTypeInfo(typeof(TResponse));
        return RequestCoreAsync(topic, key, payload, payloadType, responseType, subsystems, timeout, cancellationToken);
    }

    /// <summary>Sends one trim-safe request and returns one outcome per subsystem.</summary>
    /// <remarks>
    /// A missed deadline returns <see cref="TimeoutError"/> for that subsystem.
    /// A request-level failure throws instead of returning a partial dictionary.
    /// </remarks>
    /// <exception cref="ArgumentException">A subsystem name is invalid.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The timeout is negative.</exception>
    /// <exception cref="OperationCanceledException">The cancellation token was canceled.</exception>
    public Task<IReadOnlyDictionary<string, Outcome<TResponse>>> RequestAsync<TPayload, TResponse>(
        string topic,
        string key,
        TPayload payload,
        JsonTypeInfo<TPayload> payloadType,
        JsonTypeInfo<TResponse> responseType,
        IReadOnlyList<string> subsystems,
        TimeSpan timeout,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(topic);
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(payloadType);
        ArgumentNullException.ThrowIfNull(responseType);
        ArgumentNullException.ThrowIfNull(subsystems);
        cancellationToken.ThrowIfCancellationRequested();
        return RequestCoreAsync(topic, key, payload, payloadType, responseType, subsystems, timeout, cancellationToken);
    }

    private async Task<IReadOnlyDictionary<string, Outcome<TResponse>>> RequestCoreAsync<TPayload, TResponse>(
        string topic,
        string key,
        TPayload payload,
        JsonTypeInfo<TPayload> payloadType,
        JsonTypeInfo<TResponse> responseType,
        IReadOnlyList<string> subsystems,
        TimeSpan timeout,
        CancellationToken cancellationToken
    )
    {
        if (timeout < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout), timeout, "A duration cannot be negative.");
        }
        var native = await NativeAsync(cancellationToken).ConfigureAwait(false);
        var encoded = JsonSerializer.SerializeToUtf8Bytes(payload, payloadType);
        var (eventId, eventType) = TypedEventMetadataExtractor.Extract(payload, payloadType);
        // Standard propagation can add traceparent, tracestate, and baggage.
        var carrier = new Dictionary<string, string>(capacity: 3, StringComparer.OrdinalIgnoreCase);
        TracePropagation.Inject(carrier);
        var request = new Native.NativeRequest(
            topic,
            key,
            encoded,
            new Native.EventMetadata(EventId: eventId, EventType: eventType),
            [.. subsystems],
            timeout,
            carrier
        );
        return await CompleteRequestAsync(
                responseType,
                signal => native.Request(request, signal),
                nameof(subsystems),
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    /// <summary>Sends one excise request and returns one outcome per subsystem.</summary>
    [RequiresUnreferencedCode(Trimming.JsonMetadata)]
    [RequiresDynamicCode(Trimming.JsonMetadata)]
    public Task<IReadOnlyDictionary<string, Outcome<TResponse>>> RequestExciseAsync<TResponse>(
        string topic,
        string key,
        IReadOnlyList<string> subsystems,
        TimeSpan timeout,
        CancellationToken cancellationToken = default
    ) =>
        RequestExciseAsync(
            topic,
            key,
            (JsonTypeInfo<TResponse>)JsonOptions.GetTypeInfo(typeof(TResponse)),
            subsystems,
            timeout,
            cancellationToken
        );

    /// <summary>Sends one trim-safe excise request and returns one outcome per subsystem.</summary>
    public async Task<IReadOnlyDictionary<string, Outcome<TResponse>>> RequestExciseAsync<TResponse>(
        string topic,
        string key,
        JsonTypeInfo<TResponse> responseType,
        IReadOnlyList<string> subsystems,
        TimeSpan timeout,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(topic);
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(responseType);
        ArgumentNullException.ThrowIfNull(subsystems);
        cancellationToken.ThrowIfCancellationRequested();
        if (timeout < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout), timeout, "A duration cannot be negative.");
        }
        var carrier = new Dictionary<string, string>(capacity: 3, StringComparer.OrdinalIgnoreCase);
        TracePropagation.Inject(carrier);
        var request = new Native.NativeExciseRequest(topic, key, [.. subsystems], timeout, carrier);
        var native = await NativeAsync(cancellationToken).ConfigureAwait(false);
        return await CompleteRequestAsync(
                responseType,
                signal => native.RequestExcise(request, signal),
                nameof(subsystems),
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    private static async Task<IReadOnlyDictionary<string, Outcome<TResponse>>> CompleteRequestAsync<TResponse>(
        JsonTypeInfo<TResponse> responseType,
        Func<Native.CancellationSignal?, Task<Dictionary<string, Native.NativeRequestResult>>> send,
        string subsystemParameterName,
        CancellationToken cancellationToken
    )
    {
        LinkedCancellationSignal? linked = CancellationHelper.CreateSignal(cancellationToken);
        Dictionary<string, Native.NativeRequestResult> nativeResults;
        try
        {
            nativeResults = await send(linked?.Signal).ConfigureAwait(false);
        }
        catch (Native.FfiException.Cancelled ex)
        {
            throw new OperationCanceledException("The request was cancelled.", ex, cancellationToken);
        }
        catch (Native.FfiException.PermanentState ex)
        {
            throw new ArgumentException(ex.Message, subsystemParameterName, ex);
        }
        finally
        {
            if (linked is { } value)
            {
                await value.Registration.DisposeAsync().ConfigureAwait(false);
                value.Signal.Dispose();
            }
        }
        var outcomes = new Dictionary<string, Outcome<TResponse>>(nativeResults.Count, StringComparer.Ordinal);
        foreach (var (subsystem, result) in nativeResults)
        {
            outcomes.Add(subsystem, MapOutcome(result, responseType));
        }
        return outcomes;
    }

    internal static Outcome<T> MapOutcome<T>(Native.NativeRequestResult result, JsonTypeInfo<T> responseType) =>
        result switch
        {
            Native.NativeRequestResult.Ok ok => DecodeResult(ok.Value, responseType),
            Native.NativeRequestResult.HandlerError error => new Failure<T>(new HandlerError(error.Message)),
            Native.NativeRequestResult.Timeout error => new Failure<T>(new TimeoutError(error.Message)),
            Native.NativeRequestResult.FormatMismatch error => new Failure<T>(new FormatMismatchError(error.Message)),
            Native.NativeRequestResult.Malformed error => new Failure<T>(new MalformedResponseError(error.Message)),
            _ => throw new InvalidOperationException("Unknown response result"),
        };

    private static Outcome<T> DecodeResult<T>(byte[] value, JsonTypeInfo<T> responseType)
    {
        try
        {
            return new Success<T>(JsonSerializer.Deserialize(value.AsSpan(), responseType)!);
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        {
            return new Failure<T>(new MalformedResponseError(exception.Message));
        }
    }
}
