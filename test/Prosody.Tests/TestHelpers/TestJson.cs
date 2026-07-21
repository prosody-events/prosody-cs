using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Prosody.Tests.TestHelpers;

/// <summary>
/// Shared JSON serialization scaffolding for the unit suite: web-defaults options backed by the
/// reflection-based resolver and a helper that resolves the runtime <see cref="JsonTypeInfo{T}"/> the
/// state and message surfaces require.
/// </summary>
internal static class TestJson
{
    /// <summary>Web-defaults options with a reflection resolver, mirroring the client's JSON path.</summary>
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
    };

    /// <summary>Resolves the runtime type metadata for <typeparamref name="T"/> from <see cref="Options"/>.</summary>
    public static JsonTypeInfo<T> TypeInfo<T>() => (JsonTypeInfo<T>)Options.GetTypeInfo(typeof(T));
}
