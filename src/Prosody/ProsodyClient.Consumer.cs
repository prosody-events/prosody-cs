using System.Diagnostics.CodeAnalysis;
using Prosody.Configuration;
using Prosody.Errors;
using Prosody.Infrastructure;
using Prosody.Messaging;

namespace Prosody;

// This file owns the consumer members: subscribe, unsubscribe, and consumer health.

public sealed partial class ProsodyClient
{
    /// <summary>
    /// Gets the current consumer state.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the consumer configuration failed during build, with the full error message.
    /// </exception>
    public async Task<ConsumerState> GetConsumerStateAsync()
    {
        Native.ConsumerState state = await _native.ConsumerState();
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
    public Task<uint> AssignedPartitionCountAsync() => _native.AssignedPartitionCount();

    /// <summary>
    /// Gets a value indicating whether the consumer is currently stalled.
    /// </summary>
    public Task<bool> IsStalledAsync() => _native.IsStalled();

    /// <summary>
    /// Subscribes to receive messages using the provided strongly typed event handler.
    /// </summary>
    /// <typeparam name="TPayload">The message payload type.</typeparam>
    /// <param name="handler">The event handler to process messages and timers.</param>
    /// <remarks>
    /// <para>
    /// The payload is deserialized once into <see cref="Message{T}.Payload"/> before the
    /// handler is invoked. For topics with dynamic or mixed schemas, use
    /// <c>TPayload = <see cref="System.Text.Json.JsonElement"/></c>.
    /// </para>
    /// <para>
    /// This overload reads <c>PermanentErrorAttribute</c> from handler methods via
    /// <c>Type.GetInterfaceMap</c> (BCL-annotated and AOT-compatible at the BCL level).
    /// It is annotated with <c>[RequiresUnreferencedCode]</c>/<c>[RequiresDynamicCode]</c>
    /// because the trimmer cannot propagate DAM requirements through an interface-typed
    /// parameter to satisfy call-site annotation requirements. Use the
    /// <c>SubscribeAsync&lt;TPayload&gt;(IProsodyHandler&lt;TPayload&gt;, IPermanentErrorClassifier)</c>
    /// overload for explicit, zero-reflection error classification.
    /// </para>
    /// </remarks>
    [RequiresUnreferencedCode(
        "Reads PermanentErrorAttribute from handler methods via reflection. Use SubscribeAsync(handler, classifier) to avoid the reflection path."
    )]
    [RequiresDynamicCode(
        "GetInterfaceMap requires handler type methods to be preserved. Use SubscribeAsync(handler, classifier) to avoid this requirement."
    )]
    public Task SubscribeAsync<TPayload>(IProsodyHandler<TPayload> handler) =>
        _native.Subscribe(new EventHandlerBridge<TPayload>(handler, JsonOptions, _stateDefinitions));

    /// <summary>Subscribes with a handler that returns subsystem responses.</summary>
    [RequiresUnreferencedCode("Reads PermanentErrorAttribute from handler methods and resolves JSON metadata.")]
    [RequiresDynamicCode("Resolves handler methods and JSON metadata at run time.")]
    public Task SubscribeAsync<TPayload, TResponse>(IProsodyRequestHandler<TPayload, TResponse> handler) =>
        _native.Subscribe(EventHandlerBridge<TPayload>.Responding(handler, JsonOptions, _stateDefinitions));

    /// <summary>Subscribes with a response handler and an explicit error classifier.</summary>
    /// <remarks>This overload does not inspect <see cref="PermanentErrorAttribute"/>.</remarks>
    public Task SubscribeAsync<TPayload, TResponse>(
        IProsodyRequestHandler<TPayload, TResponse> handler,
        IPermanentErrorClassifier classifier
    ) =>
        _native.Subscribe(EventHandlerBridge<TPayload>.Responding(handler, JsonOptions, _stateDefinitions, classifier));

    /// <summary>
    /// Subscribes to receive messages using the provided strongly typed event handler and
    /// an explicit error classifier (zero reflection; no attribute lookup is performed).
    /// </summary>
    /// <typeparam name="TPayload">The message payload type.</typeparam>
    /// <param name="handler">The event handler to process messages and timers.</param>
    /// <param name="classifier">
    /// Classifies exceptions thrown by <paramref name="handler"/> as permanent or transient.
    /// Bypasses the reflection-based <c>PermanentErrorAttribute</c> lookup entirely.
    /// </param>
    /// <remarks>
    /// Use this overload when you want full control over error classification or want to avoid
    /// the reflection path entirely. Pair with a source-generated <c>JsonSerializerContext</c>
    /// (via <see cref="ClientOptions.ConfigureJsonOptions"/>) when building for a fully
    /// zero-reflection payload deserialization path as well.
    /// </remarks>
    public Task SubscribeAsync<TPayload>(IProsodyHandler<TPayload> handler, IPermanentErrorClassifier classifier) =>
        _native.Subscribe(new EventHandlerBridge<TPayload>(handler, JsonOptions, classifier, _stateDefinitions));

    /// <summary>
    /// Stops the consumer. You can subscribe again later.
    /// </summary>
    public Task UnsubscribeAsync() => _native.Unsubscribe();
}
