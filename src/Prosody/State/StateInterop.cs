using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Prosody.Infrastructure;

namespace Prosody.State;

/// <summary>
/// Internal glue between the public keyed-state surface and the generated native handles: error
/// translation, carrier construction, cancellation-honoring dispatch, and JSON item marshaling.
/// </summary>
internal static class StateInterop
{
    /// <summary>
    /// Translates a native state failure into the matching public state exception, recovering the
    /// category from the generated exception <b>type</b>. An untagged native error passes through
    /// unchanged (it is not a categorized state error).
    /// </summary>
    internal static Exception Translate(Native.FfiException error) =>
        error switch
        {
            Native.FfiException.PermanentState permanent => new PermanentStateException(permanent.Message, permanent),
            Native.FfiException.TransientState transient => new TransientStateException(transient.Message, transient),
            _ => error,
        };

    /// <summary>
    /// Runs one asynchronous native state operation, honoring cancellation at entry and translating a
    /// categorized failure. An already-dispatched native op is awaited to completion and never
    /// abandoned, so no further op races it on the same context.
    /// </summary>
    internal static async Task RunAsync(Func<Task> operation, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            await operation().ConfigureAwait(false);
        }
        catch (Native.FfiException ex)
        {
            throw Translate(ex);
        }
    }

    /// <summary>
    /// Runs one asynchronous native state operation that produces a value, honoring cancellation at
    /// entry and translating a categorized failure.
    /// </summary>
    internal static async Task<TResult> RunAsync<TResult>(
        Func<Task<TResult>> operation,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            return await operation().ConfigureAwait(false);
        }
        catch (Native.FfiException ex)
        {
            throw Translate(ex);
        }
    }

    /// <summary>
    /// Runs one synchronous native call (a handle vend or a scan open), translating a categorized
    /// failure into the matching public state exception.
    /// </summary>
    internal static TResult RunSync<TResult>(Func<TResult> operation)
    {
        try
        {
            return operation();
        }
        catch (Native.FfiException ex)
        {
            throw Translate(ex);
        }
    }

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
        var carrier = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        TracePropagation.Inject(carrier);
        return carrier;
    }

    /// <summary>Resolves the JSON type metadata for <typeparamref name="T"/> from the client options.</summary>
    internal static JsonTypeInfo<T> ResolveTypeInfo<T>(JsonSerializerOptions options) =>
        (JsonTypeInfo<T>)options.GetTypeInfo(typeof(T));

    /// <summary>
    /// Serializes a JSON value to raw bytes, rejecting a <see langword="null"/> (or null-serializing)
    /// value before it crosses the boundary. The remediation clause names the delete verb for the
    /// collection (for example <c>ClearAsync</c> or <c>RemoveAsync</c>).
    /// </summary>
    internal static byte[] SerializeJsonOrThrowNull<T>(T value, JsonTypeInfo<T> typeInfo, string remediation)
    {
        if (value is null)
        {
            throw new NullValueException(
                $"Cannot write a null value: JSON null is not a storable value. {remediation}"
            );
        }

        byte[] bytes;
        try
        {
            bytes = JsonSerializer.SerializeToUtf8Bytes(value, typeInfo);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            throw new TransientStateException($"Cannot serialize the value for a keyed-state write. {remediation}", ex);
        }

        if (IsJsonNullToken(bytes))
        {
            throw new NullValueException($"Cannot write a value that serializes to JSON null. {remediation}");
        }

        return bytes;
    }

    /// <summary>
    /// Projects a native key-only scan item into its key string. A wrong-shape item classifies
    /// transient — matching the four value-bearing <c>Transform</c> methods — so the message retries
    /// rather than being silently dropped.
    /// </summary>
    internal static string ItemKey(Native.StateScanItem item) =>
        item is Native.StateScanItem.MapKey key
            ? key.Key
            : throw new TransientStateException("State scan item shape mismatch: expected a map key.");

    /// <summary>Projects a native JSON map entry into a typed key-value pair.</summary>
    internal static KeyValuePair<string, T> JsonMapEntry<T>(
        Native.StateScanItem item,
        JsonTypeInfo<T> typeInfo
    ) where T : notnull =>
        item is Native.StateScanItem.MapJson entry
            ? KeyValuePair.Create(entry.Key, DeserializeJson(entry.Bytes, typeInfo))
            : throw new TransientStateException("State scan item shape mismatch: expected a JSON map entry.");

    /// <summary>Projects a native JSON deque item into its typed value.</summary>
    internal static T JsonDequeItem<T>(Native.StateScanItem item, JsonTypeInfo<T> typeInfo)
        where T : notnull =>
        item is Native.StateScanItem.DequeJson element
            ? DeserializeJson(element.Bytes, typeInfo)
            : throw new TransientStateException("State scan item shape mismatch: expected a JSON deque element.");

    /// <summary>Projects a native JSON state item into an optional typed value.</summary>
    internal static StateValue<T> JsonToValue<T>(Native.StateItem? item, JsonTypeInfo<T> typeInfo)
        where T : notnull
    {
        switch (item)
        {
            case null:
                return StateValue<T>.None;
            case Native.StateItem.Json json:
                return new StateValue<T>(DeserializeJson(json.Bytes, typeInfo));
            default:
                throw new TransientStateException("State item shape mismatch: expected a JSON document.");
        }
    }

    internal static T DeserializeJson<T>(byte[] bytes, JsonTypeInfo<T> typeInfo)
        where T : notnull =>
        JsonSerializer.Deserialize(bytes.AsSpan(), typeInfo)
        ?? throw new TransientStateException("Stored keyed-state JSON deserialized to null.");

    private static bool IsJsonNullToken(byte[] bytes) =>
        bytes.Length == 4
        && bytes[0] == (byte)'n'
        && bytes[1] == (byte)'u'
        && bytes[2] == (byte)'l'
        && bytes[3] == (byte)'l';
}
