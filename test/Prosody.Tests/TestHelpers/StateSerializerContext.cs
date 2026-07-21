using System.Text.Json.Serialization;

namespace Prosody.Tests.TestHelpers;

/// <summary>
/// A source-generated <see cref="JsonSerializerContext"/> covering the state item type used by the
/// AOT-path test. Wiring it via <c>ConfigureJsonOptions</c> (setting <c>TypeInfoResolver</c>) proves
/// keyed-state (de)serialization resolves through source-generated metadata end-to-end, not
/// reflection.
/// </summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(SourceGenState))]
internal sealed partial class StateSerializerContext : JsonSerializerContext;

/// <summary>A payload serialized through the source-generated context in the AOT-path test.</summary>
internal sealed record SourceGenState
{
    /// <summary>A name field.</summary>
    public string Name { get; init; } = "";

    /// <summary>A count field.</summary>
    public int Count { get; init; }
}
