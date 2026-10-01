using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Prosody.Messaging;

namespace Prosody.State;

/// <summary>
/// Internal glue for message-flavoured keyed-state collections: marshals the native Kafka message
/// to and from the public <see cref="Message{T}"/>, deserializing the payload through the same JSON
/// path used for message-topic payloads.
/// </summary>
internal static class MessageInterop
{
    /// <summary>
    /// Reconstructs a public <see cref="Message{T}"/> from a native message, deserializing the
    /// payload and retaining the native handle so the message can be written back to a collection.
    /// </summary>
    internal static Message<TPayload> FromNative<TPayload>(Native.Message native, JsonTypeInfo<TPayload> typeInfo)
    {
        var bytes = native.Payload();
        var payload = JsonSerializer.Deserialize(bytes.AsSpan(), typeInfo);
        return new Message<TPayload>(
            native.Topic(),
            native.Key(),
            native.Partition(),
            native.Offset(),
            new DateTimeOffset(native.Timestamp(), TimeSpan.Zero),
            payload,
            native,
            native.SourceSystem(),
            native.ResponseRequested()
        );
    }

    /// <summary>
    /// Extracts the native handle a message collection write requires. Only a message this handler
    /// received (or read back from a message collection) carries one; anything else is a caller
    /// mistake and classifies transient.
    /// </summary>
    internal static Native.Message ToNative<TPayload>(Message<TPayload> message)
    {
        ArgumentNullException.ThrowIfNull(message);

        return message.NativeHandle
            ?? throw new TransientStateException(
                "Only a message received by this handler (or read from a message collection) can be stored."
            );
    }

    /// <summary>Runs one native read with <see cref="StateInterop.RunAsync{TResult}"/> and decodes the optional message.</summary>
    internal static Task<StateValue<Message<TPayload>>> ReadAsync<TPayload>(
        Func<Dictionary<string, string>, Task<Native.Message?>> read,
        JsonTypeInfo<TPayload> typeInfo,
        CancellationToken cancellationToken
    ) =>
        StateInterop.RunAsync(
            async carrier => MessageToValue(await read(carrier).ConfigureAwait(false), typeInfo),
            cancellationToken
        );

    /// <summary>Projects an optional native message into a typed message value.</summary>
    internal static StateValue<Message<TPayload>> MessageToValue<TPayload>(
        Native.Message? item,
        JsonTypeInfo<TPayload> typeInfo
    ) =>
        item is null
            ? StateValue<Message<TPayload>>.None
            : new StateValue<Message<TPayload>>(FromNative(item, typeInfo));
}
