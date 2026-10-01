using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Prosody.Infrastructure;

namespace Prosody.State;

/// <summary>
/// Internal glue between the public keyed-state surface and the generated native handles: carrier
/// construction, cancellation-honoring dispatch, and JSON item marshaling.
/// </summary>
internal static class StateInterop
{
    /// <summary>
    /// Runs one asynchronous native state operation with a fresh carrier and
    /// <see cref="NativeErrors.RunAsync(Func{Task}, string?)"/>. A cancelled token faults the task with
    /// <see cref="OperationCanceledException"/> and starts no operation. A started native operation
    /// always runs to completion, so no later operation races it on the same context.
    /// </summary>
    internal static async Task RunAsync(
        Func<Dictionary<string, string>, Task> operation,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        await NativeErrors.RunAsync(() => operation(CreateCarrier())).ConfigureAwait(false);
    }

    /// <summary>Runs one asynchronous native state operation that produces a value, like <see cref="RunAsync(Func{Dictionary{string, string}, Task}, CancellationToken)"/>.</summary>
    internal static async Task<TResult> RunAsync<TResult>(
        Func<Dictionary<string, string>, Task<TResult>> operation,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await NativeErrors.RunAsync(() => operation(CreateCarrier())).ConfigureAwait(false);
    }

    /// <summary>Runs one native commit or rollback and converts its outcome.</summary>
    internal static Task<StoreOutcome> RunOutcomeAsync(
        Func<Dictionary<string, string>, Task<Native.StoreOutcome>> operation,
        CancellationToken cancellationToken
    ) => RunAsync(async carrier => ToPublic(await operation(carrier).ConfigureAwait(false)), cancellationToken);

    /// <summary>Opens a lazy scan of the keys that <paramref name="query"/> selects.</summary>
    internal static IAsyncEnumerable<string> Keys(
        Func<Native.KeyQuery, Native.IKeyCursor> open,
        KeyQuery query,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();
        var native = KeyQuery.ToNative(query);
        return new StateScanSequence<Native.IKeyCursor, string, string>(
            () => NativeErrors.Run(() => open(native)),
            static (cursor, carrier) => cursor.NextChunk(carrier),
            static cursor => cursor.Close(),
            static key => key,
            cancellationToken
        );
    }

    /// <summary>
    /// Validates a query limit. The exception names <paramref name="property"/>, the query property
    /// that received the value.
    /// </summary>
    internal static int? PositiveLimit(int? value, string property) =>
        value is <= 0 ? throw new ArgumentOutOfRangeException(property, value, $"{property} must be positive.") : value;

    /// <summary>
    /// Reads the deque element <paramref name="fromEnd"/> places before the end. The position is the
    /// count minus <paramref name="fromEnd"/>. A position before the front reads absent.
    /// </summary>
    internal static async Task<StateValue<T>> GetFromEndAsync<T>(
        int fromEnd,
        Func<Task<int>> count,
        Func<int, Task<StateValue<T>>> get
    )
        where T : notnull
    {
        var position = await count().ConfigureAwait(false) - fromEnd;
        return position < 0 ? StateValue<T>.None : await get(position).ConfigureAwait(false);
    }

    /// <summary>Maps a native store outcome to the public enum.</summary>
    internal static StoreOutcome ToPublic(Native.StoreOutcome outcome) =>
        outcome switch
        {
            Native.StoreOutcome.Applied => StoreOutcome.Applied,
            Native.StoreOutcome.NoOp => StoreOutcome.NoOp,
            _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "Unknown native store outcome."),
        };

    /// <summary>
    /// Maps a public scan direction to the native enum. An out-of-range value is a caller mistake
    /// and classifies transient.
    /// </summary>
    internal static Native.ScanDirection ToNative(ScanDirection direction) =>
        direction switch
        {
            ScanDirection.Forward => Native.ScanDirection.Forward,
            ScanDirection.Backward => Native.ScanDirection.Backward,
            _ => throw new TransientStateException($"Invalid scan direction: {direction}."),
        };

    /// <summary>Creates a fresh trace-propagation carrier for one native operation.</summary>
    internal static Dictionary<string, string> CreateCarrier()
    {
        // Standard propagation adds at most traceparent, tracestate, and baggage.
        var carrier = new Dictionary<string, string>(capacity: 3, StringComparer.OrdinalIgnoreCase);
        TracePropagation.Inject(carrier);
        return carrier;
    }

    /// <summary>Resolves the JSON type metadata for <typeparamref name="T"/> from the client options.</summary>
    internal static JsonTypeInfo<T> ResolveTypeInfo<T>(JsonSerializerOptions options) =>
        (JsonTypeInfo<T>)options.GetTypeInfo(typeof(T));

    /// <summary>
    /// Serializes a value for a keyed-state write. A value with no JSON form is a caller mistake and
    /// classifies transient. Prosody rejects a JSON <see langword="null"/> as permanent.
    /// </summary>
    internal static byte[] SerializeJson<T>(T value, JsonTypeInfo<T> typeInfo)
    {
        try
        {
            return JsonSerializer.SerializeToUtf8Bytes(value, typeInfo);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            throw new TransientStateException("Cannot serialize the value for a keyed-state write.", ex);
        }
    }

    /// <summary>Projects a native JSON map entry into a typed key-value pair.</summary>
    internal static KeyValuePair<string, T> JsonMapEntry<T>(Native.JsonMapEntry item, JsonTypeInfo<T> typeInfo)
        where T : notnull => KeyValuePair.Create(item.Key, DeserializeJson(item.Bytes, typeInfo));

    /// <summary>Runs one native read with <see cref="RunAsync{TResult}"/> and decodes the optional JSON item.</summary>
    internal static Task<StateValue<T>> ReadJsonAsync<T>(
        Func<Dictionary<string, string>, Task<byte[]?>> read,
        JsonTypeInfo<T> typeInfo,
        CancellationToken cancellationToken
    )
        where T : notnull =>
        RunAsync(async carrier => JsonToValue(await read(carrier).ConfigureAwait(false), typeInfo), cancellationToken);

    /// <summary>Projects optional JSON bytes into a typed value.</summary>
    internal static StateValue<T> JsonToValue<T>(byte[]? bytes, JsonTypeInfo<T> typeInfo)
        where T : notnull => bytes is null ? StateValue<T>.None : new StateValue<T>(DeserializeJson(bytes, typeInfo));

    internal static T DeserializeJson<T>(byte[] bytes, JsonTypeInfo<T> typeInfo)
        where T : notnull =>
        JsonSerializer.Deserialize(bytes.AsSpan(), typeInfo)
        ?? throw new TransientStateException("Stored keyed-state JSON deserialized to null.");
}
