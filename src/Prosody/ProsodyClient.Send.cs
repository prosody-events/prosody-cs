using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Prosody.Configuration;
using Prosody.Infrastructure;
using Prosody.State;

namespace Prosody;

// This file owns the members that send messages and excise records.

public sealed partial class ProsodyClient
{
    /// <summary>The send options for a send without metadata overrides.</summary>
    private static readonly SendOptions NoOverrides = new();

    /// <summary>
    /// Sends a message to a topic, serializing <paramref name="payload"/> with the client's
    /// configured <see cref="JsonSerializerOptions"/>.
    /// </summary>
    /// <typeparam name="T">The type of the payload to serialize as JSON.</typeparam>
    /// <param name="topic">The topic to send to.</param>
    /// <param name="key">The message key.</param>
    /// <param name="payload">The message payload (will be serialized to JSON).</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    /// <remarks>
    /// <para>
    /// Resolves <see cref="JsonTypeInfo{T}"/> from the client's configured options. If those
    /// options use <c>DefaultJsonTypeInfoResolver</c> (the default when no resolver is set via
    /// <see cref="ClientOptions.ConfigureJsonOptions"/>), this call uses reflection metadata.
    /// For trim-safe publishing, use the <c>SendAsync&lt;T&gt;(string, string, T, JsonTypeInfo&lt;T&gt;, CancellationToken)</c>
    /// overload and pass a source-generated <see cref="JsonTypeInfo{T}"/> directly.
    /// </para>
    /// <para>
    /// If <typeparamref name="T"/> exposes lowercase <c>id</c> or <c>type</c> string
    /// properties (matched by <see cref="JsonPropertyNameAttribute"/> or by exact CLR
    /// name), their values are forwarded as event metadata so the producer's idempotence
    /// dedup and downstream <c>allowed_events</c> filtering see them without re-parsing
    /// the JSON. PascalCase properties (<c>Id</c>, <c>Type</c>) must use
    /// <c>[JsonPropertyName("id")]</c> to participate, matching the lowercase wire
    /// contract the rest of the system requires.
    /// </para>
    /// </remarks>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is cancelled before or during the send.</exception>
    [RequiresUnreferencedCode(
        "Resolves JsonTypeInfo<T> from the client's options resolver, which may use DefaultJsonTypeInfoResolver (reflection-based). Use the SendAsync overload that accepts JsonTypeInfo<T> for trim-safe publishing."
    )]
    [RequiresDynamicCode(
        "Resolves JsonTypeInfo<T> from the client's options resolver, which may use DefaultJsonTypeInfoResolver. Use the SendAsync overload that accepts JsonTypeInfo<T> for trim-safe publishing."
    )]
    public Task SendAsync<T>(string topic, string key, T payload, CancellationToken cancellationToken = default) =>
        SendAsync(topic, key, payload, StateInterop.ResolveTypeInfo<T>(JsonOptions), cancellationToken);

    /// <summary>
    /// Sends a message to a topic, serializing <paramref name="payload"/> using the supplied
    /// <paramref name="typeInfo"/> (trim-safe; no reflection resolver is consulted).
    /// </summary>
    /// <typeparam name="T">The type of the payload to serialize as JSON.</typeparam>
    /// <param name="topic">The topic to send to.</param>
    /// <param name="key">The message key.</param>
    /// <param name="payload">The message payload (will be serialized to JSON).</param>
    /// <param name="typeInfo">
    /// Source-generated <see cref="JsonTypeInfo{T}"/> for <typeparamref name="T"/>.
    /// Use a source-generated <c>JsonSerializerContext</c> to obtain one:
    /// <c>AppJsonContext.Default.MyType</c>.
    /// </param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    /// <remarks>
    /// <para>
    /// Event metadata (<c>id</c> and <c>type</c>) is extracted by walking the
    /// <paramref name="typeInfo"/>'s property list. If your source-generated context
    /// uses a naming policy that does not produce lowercase <c>"id"</c>/<c>"type"</c>
    /// property names, extraction will silently yield <c>null</c>. In that scenario,
    /// use the overload that accepts <see cref="SendOptions"/> to provide explicit
    /// <see cref="SendOptions.EventId"/> and <see cref="SendOptions.EventType"/> values.
    /// </para>
    /// </remarks>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is cancelled before or during the send.</exception>
    public Task SendAsync<T>(
        string topic,
        string key,
        T payload,
        JsonTypeInfo<T> typeInfo,
        CancellationToken cancellationToken = default
    ) => SendAsync(topic, key, payload, typeInfo, NoOverrides, cancellationToken);

    /// <summary>
    /// Sends a message to a topic, serializing <paramref name="payload"/> using the supplied
    /// <paramref name="typeInfo"/> with explicit metadata overrides (trim-safe).
    /// </summary>
    /// <typeparam name="T">The type of the payload to serialize as JSON.</typeparam>
    /// <param name="topic">The topic to send to.</param>
    /// <param name="key">The message key.</param>
    /// <param name="payload">The message payload (will be serialized to JSON).</param>
    /// <param name="typeInfo">
    /// Source-generated <see cref="JsonTypeInfo{T}"/> for <typeparamref name="T"/>.
    /// </param>
    /// <param name="options">
    /// Per-message overrides. When <see cref="SendOptions.EventId"/> or
    /// <see cref="SendOptions.EventType"/> is set, that value is used instead of
    /// extracting from the payload.
    /// </param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is cancelled before or during the send.</exception>
    public Task SendAsync<T>(
        string topic,
        string key,
        T payload,
        JsonTypeInfo<T> typeInfo,
        SendOptions options,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(topic);
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(typeInfo);
        ArgumentNullException.ThrowIfNull(options);
        cancellationToken.ThrowIfCancellationRequested();

        return SendCoreAsync(topic, key, payload, typeInfo, options, cancellationToken);
    }

    /// <summary>Sends an excise record for a key.</summary>
    public async Task ExciseAsync(string topic, string key, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(topic);
        ArgumentNullException.ThrowIfNull(key);
        cancellationToken.ThrowIfCancellationRequested();

        var carrier = StateInterop.CreateCarrier();
        await CancellationHelper
            .RunAsync(
                signal => _native.Excise(topic, key, carrier, signal),
                "The excise was cancelled.",
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    private async Task SendCoreAsync<T>(
        string topic,
        string key,
        T payload,
        JsonTypeInfo<T> typeInfo,
        SendOptions options,
        CancellationToken cancellationToken
    )
    {
        var (extractedId, extractedType) = TypedEventMetadataExtractor.Extract(payload, typeInfo);
        var metadata = new Native.EventMetadata(
            EventId: options.EventId ?? extractedId,
            EventType: options.EventType ?? extractedType
        );
        var jsonBytes = JsonSerializer.SerializeToUtf8Bytes(payload, typeInfo);
        var carrier = StateInterop.CreateCarrier();

        await CancellationHelper
            .RunAsync(
                signal => _native.Send(topic, key, metadata, jsonBytes, carrier, signal),
                "The send was cancelled.",
                cancellationToken
            )
            .ConfigureAwait(false);
    }
}
