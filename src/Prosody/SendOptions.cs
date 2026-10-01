using System.Text.Json.Serialization.Metadata;
using Prosody.Infrastructure;

namespace Prosody;

/// <summary>
/// Optional per-message overrides for
/// <see cref="ProsodyClient.SendAsync{T}(string, string, T, JsonTypeInfo{T}, SendOptions, CancellationToken)"/> and
/// <see cref="ProsodyClient.RequestAsync{TPayload, TResponse}(string, string, TPayload, JsonTypeInfo{TPayload}, JsonTypeInfo{TResponse}, IReadOnlyList{string}, TimeSpan, SendOptions, CancellationToken)"/>.
/// </summary>
/// <remarks>
/// <para>
/// By default, Prosody extracts <c>id</c> and <c>type</c> metadata from the payload's
/// JSON properties (as resolved by the supplied <see cref="JsonTypeInfo{T}"/>).
/// Use this record to override those values explicitly — for example, when your
/// source-generated <c>JsonSerializerContext</c> uses a naming policy that differs from
/// the lowercase <c>"id"</c>/<c>"type"</c> convention the metadata extractor expects.
/// </para>
/// </remarks>
public sealed record SendOptions
{
    /// <summary>
    /// Explicit event ID. When set, bypasses automatic extraction from the payload's
    /// <c>id</c> property.
    /// </summary>
    public string? EventId { get; init; }

    /// <summary>
    /// Explicit event type. When set, bypasses automatic extraction from the payload's
    /// <c>type</c> property.
    /// </summary>
    public string? EventType { get; init; }

    /// <summary>
    /// Builds the native event metadata for <paramref name="payload"/>. A set option replaces the
    /// value that the payload carries.
    /// </summary>
    internal Native.EventMetadata Metadata<T>(T payload, JsonTypeInfo<T> typeInfo)
    {
        var (extractedId, extractedType) = TypedEventMetadataExtractor.Extract(payload, typeInfo);
        return new(EventId: EventId ?? extractedId, EventType: EventType ?? extractedType);
    }
}
