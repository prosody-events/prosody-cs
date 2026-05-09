using System.Runtime.CompilerServices;
using System.Text.Json.Serialization.Metadata;

namespace Prosody.Infrastructure;

/// <summary>
/// Pulls the JSON <c>id</c> and <c>type</c> string fields out of a typed payload by
/// walking <see cref="JsonTypeInfo.Properties"/> — the same metadata
/// <see cref="System.Text.Json.JsonSerializer"/> uses to emit the wire bytes.
/// </summary>
/// <remarks>
/// <para>
/// Because the contract is shared with the serializer, the extracted values are exactly
/// what would appear on the wire. A property is matched when its
/// <see cref="JsonPropertyInfo.Name"/> is exactly <c>"id"</c> or <c>"type"</c>:
/// </para>
/// <list type="bullet">
///   <item>Properties carrying <c>[JsonPropertyName("id")]</c> resolve to that name directly.</item>
///   <item>Properties whose CLR name is <c>id</c> or <c>type</c> resolve to themselves under the default identity naming policy.</item>
///   <item>PascalCase properties (<c>Id</c>, <c>Type</c>) without <c>[JsonPropertyName]</c> do not match — they would serialize as <c>"Id"</c> / <c>"Type"</c>.</item>
///   <item><c>[JsonIgnore]</c> properties are absent from <see cref="JsonTypeInfo.Properties"/>, so they're naturally skipped.</item>
///   <item>Write-only properties (no getter) are skipped.</item>
/// </list>
/// <para>
/// Getters are cached per <see cref="JsonTypeInfo"/> instance via a
/// <see cref="ConditionalWeakTable{TKey,TValue}"/>, so entries are collected when the
/// corresponding <see cref="System.Text.Json.JsonSerializerOptions"/> is GC'd.
/// </para>
/// </remarks>
internal static class TypedEventMetadataExtractor
{
    private static readonly ConditionalWeakTable<JsonTypeInfo, Getters> Cache = new();

    internal static (string? Id, string? Type) Extract<T>(T value, JsonTypeInfo<T> typeInfo)
    {
        if (value is null)
            return (null, null);

        var getters = Cache.GetValue(typeInfo, static ti => BuildGetters(ti));
        return (getters.IdGet?.Invoke(value) as string, getters.TypeGet?.Invoke(value) as string);
    }

    private static Getters BuildGetters(JsonTypeInfo typeInfo)
    {
        Func<object, object?>? idGet = null;
        Func<object, object?>? typeGet = null;

        foreach (JsonPropertyInfo prop in typeInfo.Properties)
        {
            if (prop.PropertyType != typeof(string) || prop.Get is null)
                continue;

            if (idGet is null && string.Equals(prop.Name, "id", StringComparison.Ordinal))
                idGet = prop.Get;
            else if (typeGet is null && string.Equals(prop.Name, "type", StringComparison.Ordinal))
                typeGet = prop.Get;

            if (idGet is not null && typeGet is not null)
                break;
        }

        return new Getters(idGet, typeGet);
    }

    private sealed record Getters(Func<object, object?>? IdGet, Func<object, object?>? TypeGet);
}
