using System.Reflection;
using System.Text.Json.Serialization;

namespace Prosody.Infrastructure;

/// <summary>
/// Pulls the JSON <c>id</c> and <c>type</c> string fields out of a typed
/// payload <typeparamref name="T"/> by reading public string properties
/// directly off the object — no JSON parse step.
/// </summary>
/// <remarks>
/// <para>
/// Matching is case-sensitive against the lowercase JSON names <c>id</c> and
/// <c>type</c>, mirroring the downstream consumer's JSON extractor which
/// reads those fields verbatim from the wire. PascalCase CLR names like
/// <c>Id</c> or <c>Type</c> do not match unless re-mapped via
/// <c>[JsonPropertyName("id")]</c>.
/// </para>
/// <para>
/// A property is matched when either:
/// </para>
/// <list type="bullet">
///   <item>
///     It carries <see cref="JsonPropertyNameAttribute"/> whose name is
///     exactly <c>"id"</c> or <c>"type"</c>, or
///   </item>
///   <item>
///     It has no <see cref="JsonPropertyNameAttribute"/> and its CLR name is
///     exactly <c>id</c> or <c>type</c>.
///   </item>
/// </list>
/// <para>
/// Attribute matches always take precedence over name matches, so the result
/// is independent of <see cref="System.Type.GetProperties()"/> ordering.
/// Properties annotated with <c>[JsonIgnore]</c> (default condition
/// <see cref="JsonIgnoreCondition.Always"/>) are skipped so the metadata
/// reflects what the JSON actually contains.
/// </para>
/// <para>
/// The matched <see cref="PropertyInfo"/> is cached per closed generic
/// <typeparamref name="T"/>; reflection runs once per type for the lifetime
/// of the assembly.
/// </para>
/// </remarks>
internal static class TypedEventMetadataExtractor<T>
{
    private static readonly PropertyInfo? IdProperty = FindProperty("id");
    private static readonly PropertyInfo? TypeProperty = FindProperty("type");

    internal static (string? Id, string? Type) Extract(T value) =>
        value is null ? (null, null) : (IdProperty?.GetValue(value) as string, TypeProperty?.GetValue(value) as string);

    private static PropertyInfo? FindProperty(string jsonName)
    {
        PropertyInfo? attributeMatch = null;
        PropertyInfo? nameMatch = null;

        foreach (PropertyInfo prop in typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (prop.PropertyType != typeof(string))
            {
                continue;
            }
            if (prop.GetCustomAttribute<JsonIgnoreAttribute>() is { Condition: JsonIgnoreCondition.Always })
            {
                continue;
            }

            JsonPropertyNameAttribute? attr = prop.GetCustomAttribute<JsonPropertyNameAttribute>();
            if (attr is not null)
            {
                if (string.Equals(attr.Name, jsonName, StringComparison.Ordinal))
                {
                    attributeMatch ??= prop;
                }
            }
            else if (string.Equals(prop.Name, jsonName, StringComparison.Ordinal))
            {
                nameMatch ??= prop;
            }
        }

        return attributeMatch ?? nameMatch;
    }
}
