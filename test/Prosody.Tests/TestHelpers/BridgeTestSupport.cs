using System.Diagnostics.CodeAnalysis;
using Prosody.Errors;
using Prosody.Infrastructure;
using Prosody.Messaging;
using static Prosody.Tests.TestHelpers.TestDefaults;
using NativeResult = Prosody.Native.HandlerResult;

namespace Prosody.Tests.TestHelpers;

/// <summary>
/// Shared fixtures for the <see cref="EventHandlerBridge{TPayload}"/> unit tests. The helpers call the
/// internal handle methods with placeholder metadata, so no test crosses into the native library.
/// </summary>
internal static class BridgeTestSupport
{
    /// <summary>A placeholder payload for tests that do not inspect the message contents.</summary>
    public static readonly byte[] AnyJson = "null"u8.ToArray();

    public static readonly ProsodyContext AnyContext = new();

    public static readonly ProsodyTimer AnyTimer = new("test-key", default);

    /// <summary>Handles one message from <c>test-topic</c> with key <c>test-key</c>.</summary>
    public static Task<NativeResult> HandleMessageAsync<T>(
        EventHandlerBridge<T> bridge,
        byte[]? payload = null,
        Func<Task>? onCancel = null
    ) =>
        bridge.HandleMessageAsync(
            AnyContext,
            "test-topic",
            "test-key",
            partition: 1,
            offset: 2L,
            DateTimeOffset.UnixEpoch,
            payload ?? AnyJson,
            onCancel ?? NeverCancel,
            EmptyCarrier
        );

    /// <summary>Handles one fire of <see cref="AnyTimer"/>.</summary>
    public static Task<NativeResult> HandleTimerAsync<T>(EventHandlerBridge<T> bridge, Func<Task>? onCancel = null) =>
        bridge.HandleTimerAsync(AnyContext, AnyTimer, onCancel ?? NeverCancel, EmptyCarrier);
}

/// <summary>A typed payload for the bridge decoding tests.</summary>
internal sealed record BridgePayload(string Name, int Count);

/// <summary>
/// Custom exception implementing <see cref="IPermanentError"/> for testing.
/// </summary>
[SuppressMessage(
    "Design",
    "CA1032:Implement standard exception constructors",
    Justification = "Test-only exception; minimal constructors sufficient"
)]
internal sealed class CustomPermanentException(string message) : Exception(message), IPermanentError;
