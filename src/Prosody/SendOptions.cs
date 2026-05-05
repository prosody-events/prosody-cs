namespace Prosody;

/// <summary>
/// Optional per-message overrides for <see cref="ProsodyClient.SendAsync{T}(string, string, T, System.Text.Json.Serialization.Metadata.JsonTypeInfo{T}, SendOptions, System.Threading.CancellationToken)"/>.
/// </summary>
/// <remarks>
/// <para>
/// By default, Prosody extracts <c>id</c> and <c>type</c> metadata from the payload's
/// JSON properties (as resolved by the supplied <see cref="System.Text.Json.Serialization.Metadata.JsonTypeInfo{T}"/>).
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
}
