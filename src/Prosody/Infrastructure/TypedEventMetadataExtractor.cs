using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Prosody.Infrastructure;

/// <summary>
/// Pulls the JSON <c>id</c> and <c>type</c> string fields out of a typed
/// payload <typeparamref name="T"/> by walking System.Text.Json's
/// <see cref="JsonTypeInfo"/> — the same metadata <see cref="JsonSerializer"/>
/// uses to emit the wire bytes.
/// </summary>
/// <remarks>
/// <para>
/// Because the contract is shared with the serializer, the extracted values
/// are exactly what would appear on the wire (modulo a user-supplied
/// <see cref="JsonConverter"/> on <typeparamref name="T"/> that takes over
/// output entirely). A property is matched when its
/// <see cref="JsonPropertyInfo.Name"/> is exactly <c>"id"</c> or <c>"type"</c>:
/// </para>
/// <list type="bullet">
///   <item>Properties carrying <c>[JsonPropertyName("id")]</c> resolve to that name directly.</item>
///   <item>Properties whose CLR name is <c>id</c> or <c>type</c> resolve to themselves under the default identity naming policy used by <see cref="JsonSerializerOptions.Default"/>.</item>
///   <item>PascalCase properties (<c>Id</c>, <c>Type</c>) without <c>[JsonPropertyName]</c> do not match — they would serialize as <c>"Id"</c> / <c>"Type"</c>, which the downstream consumer's JSON extractor would not see either.</item>
///   <item><c>[JsonIgnore]</c> (default condition <see cref="JsonIgnoreCondition.Always"/>) properties are absent from <see cref="JsonTypeInfo.Properties"/>, so they're naturally skipped.</item>
/// </list>
/// <para>
/// The matched <see cref="JsonPropertyInfo.Get"/> delegate is cached per
/// closed generic <typeparamref name="T"/>; type-info resolution runs once
/// per type for the lifetime of the assembly.
/// </para>
/// </remarks>
internal static class TypedEventMetadataExtractor<T>
{
    private static readonly Func<object, object?>? IdGetter = FindGetter("id");
    private static readonly Func<object, object?>? TypeGetter = FindGetter("type");

    internal static (string? Id, string? Type) Extract(T value) =>
        value is null ? (null, null) : (IdGetter?.Invoke(value) as string, TypeGetter?.Invoke(value) as string);

    private static Func<object, object?>? FindGetter(string jsonName)
    {
        foreach (JsonPropertyInfo prop in JsonSerializerOptions.Default.GetTypeInfo(typeof(T)).Properties)
        {
            if (prop.PropertyType == typeof(string) && string.Equals(prop.Name, jsonName, StringComparison.Ordinal))
            {
                return prop.Get;
            }
        }
        return null;
    }
}
