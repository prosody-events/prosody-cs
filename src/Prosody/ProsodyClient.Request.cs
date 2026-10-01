using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Prosody.Errors;
using Prosody.Infrastructure;
using Prosody.Messaging;
using Prosody.State;

namespace Prosody;

// This file owns the request members, which send one event and collect one outcome per subsystem.

public sealed partial class ProsodyClient
{
    private const string _runtimeJsonMetadataWarning =
        "Resolves JSON metadata at run time. Use the overload that accepts JsonTypeInfo values.";

    /// <summary>Sends one request and returns one outcome per subsystem.</summary>
    /// <inheritdoc
    ///     cref="RequestAsync{TPayload, TResponse}(string, string, TPayload, JsonTypeInfo{TPayload}, JsonTypeInfo{TResponse}, IReadOnlyList{string}, TimeSpan, SendOptions, CancellationToken)"
    ///     path="/remarks|/exception"/>
    [RequiresUnreferencedCode(_runtimeJsonMetadataWarning)]
    [RequiresDynamicCode(_runtimeJsonMetadataWarning)]
    public Task<IReadOnlyDictionary<string, Outcome<TResponse>>> RequestAsync<TPayload, TResponse>(
        string topic,
        string key,
        TPayload payload,
        IReadOnlyList<string> subsystems,
        TimeSpan timeout,
        CancellationToken cancellationToken = default
    ) =>
        RequestAsync(
            topic,
            key,
            payload,
            StateInterop.ResolveTypeInfo<TPayload>(JsonOptions),
            StateInterop.ResolveTypeInfo<TResponse>(JsonOptions),
            subsystems,
            timeout,
            cancellationToken
        );

    /// <summary>Sends one trim-safe request and returns one outcome per subsystem.</summary>
    /// <inheritdoc
    ///     cref="RequestAsync{TPayload, TResponse}(string, string, TPayload, JsonTypeInfo{TPayload}, JsonTypeInfo{TResponse}, IReadOnlyList{string}, TimeSpan, SendOptions, CancellationToken)"
    ///     path="/remarks|/exception"/>
    public Task<IReadOnlyDictionary<string, Outcome<TResponse>>> RequestAsync<TPayload, TResponse>(
        string topic,
        string key,
        TPayload payload,
        JsonTypeInfo<TPayload> payloadType,
        JsonTypeInfo<TResponse> responseType,
        IReadOnlyList<string> subsystems,
        TimeSpan timeout,
        CancellationToken cancellationToken = default
    ) =>
        RequestAsync(
            topic,
            key,
            payload,
            payloadType,
            responseType,
            subsystems,
            timeout,
            NoOverrides,
            cancellationToken
        );

    /// <summary>
    /// Sends one trim-safe request with event metadata overrides and returns one outcome per subsystem.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A set <see cref="SendOptions.EventId"/> or <see cref="SendOptions.EventType"/> replaces the
    /// value that Prosody reads from the payload.
    /// </para>
    /// <para>
    /// A missed deadline returns <see cref="TimeoutError"/> for that subsystem.
    /// A request-level failure throws instead of returning a partial dictionary.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// The subsystem list is empty, repeats a name, or has an empty name, or the timeout is too large.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">The timeout is negative.</exception>
    /// <exception cref="InvalidOperationException">The client is shut down.</exception>
    /// <exception cref="ProsodyException">Kafka did not accept the request.</exception>
    /// <exception cref="OperationCanceledException">The cancellation token was canceled.</exception>
    public Task<IReadOnlyDictionary<string, Outcome<TResponse>>> RequestAsync<TPayload, TResponse>(
        string topic,
        string key,
        TPayload payload,
        JsonTypeInfo<TPayload> payloadType,
        JsonTypeInfo<TResponse> responseType,
        IReadOnlyList<string> subsystems,
        TimeSpan timeout,
        SendOptions options,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(topic);
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(payloadType);
        ArgumentNullException.ThrowIfNull(responseType);
        ArgumentNullException.ThrowIfNull(subsystems);
        ArgumentNullException.ThrowIfNull(options);
        cancellationToken.ThrowIfCancellationRequested();
        return RequestCoreAsync(
            topic,
            key,
            payload,
            payloadType,
            responseType,
            subsystems,
            timeout,
            options,
            cancellationToken
        );
    }

    /// <summary>Sends one excise request and returns one outcome per subsystem.</summary>
    /// <inheritdoc
    ///     cref="RequestAsync{TPayload, TResponse}(string, string, TPayload, JsonTypeInfo{TPayload}, JsonTypeInfo{TResponse}, IReadOnlyList{string}, TimeSpan, SendOptions, CancellationToken)"
    ///     path="/remarks|/exception"/>
    [RequiresUnreferencedCode(_runtimeJsonMetadataWarning)]
    [RequiresDynamicCode(_runtimeJsonMetadataWarning)]
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
            StateInterop.ResolveTypeInfo<TResponse>(JsonOptions),
            subsystems,
            timeout,
            cancellationToken
        );

    /// <summary>Sends one trim-safe excise request and returns one outcome per subsystem.</summary>
    /// <inheritdoc
    ///     cref="RequestAsync{TPayload, TResponse}(string, string, TPayload, JsonTypeInfo{TPayload}, JsonTypeInfo{TResponse}, IReadOnlyList{string}, TimeSpan, SendOptions, CancellationToken)"
    ///     path="/remarks|/exception"/>
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

        var request = new Native.NativeExciseRequest(
            topic,
            key,
            [.. subsystems],
            Durations.ToNative(timeout),
            StateInterop.CreateCarrier()
        );
        var native = await NativeAsync(cancellationToken).ConfigureAwait(false);
        return await CompleteRequestAsync(
                responseType,
                signal => native.RequestExcise(request, signal),
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    private async Task<IReadOnlyDictionary<string, Outcome<TResponse>>> RequestCoreAsync<TPayload, TResponse>(
        string topic,
        string key,
        TPayload payload,
        JsonTypeInfo<TPayload> payloadType,
        JsonTypeInfo<TResponse> responseType,
        IReadOnlyList<string> subsystems,
        TimeSpan timeout,
        SendOptions options,
        CancellationToken cancellationToken
    )
    {
        var encoded = JsonSerializer.SerializeToUtf8Bytes(payload, payloadType);
        var request = new Native.NativeRequest(
            topic,
            key,
            encoded,
            options.Metadata(payload, payloadType),
            [.. subsystems],
            Durations.ToNative(timeout),
            StateInterop.CreateCarrier()
        );
        var native = await NativeAsync(cancellationToken).ConfigureAwait(false);
        return await CompleteRequestAsync(responseType, signal => native.Request(request, signal), cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task<IReadOnlyDictionary<string, Outcome<TResponse>>> CompleteRequestAsync<TResponse>(
        JsonTypeInfo<TResponse> responseType,
        Func<Native.CancellationSignal?, Task<Dictionary<string, Native.NativeRequestResult>>> send,
        CancellationToken cancellationToken
    )
    {
        var nativeResults = await CancellationHelper
            .RunAsync(send, "The request was cancelled.", cancellationToken)
            .ConfigureAwait(false);

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
            return new Success<T>(JsonSerializer.Deserialize(value.AsSpan(), responseType));
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        {
            return new Failure<T>(new MalformedResponseError(exception.Message));
        }
    }
}
