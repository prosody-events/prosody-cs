using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Prosody.Infrastructure;

namespace Prosody.Tests.Unit;

/// <summary>
/// Tests for <see cref="TypedEventMetadataExtractor"/>: id/type extraction from
/// typed payloads via reflection, with no JSON parsing.
/// </summary>
public sealed class TypedEventMetadataExtractorTests
{
    private static JsonTypeInfo<T> DefaultTypeInfo<T>() =>
        (JsonTypeInfo<T>)JsonSerializerOptions.Default.GetTypeInfo(typeof(T));

    [Fact]
    public void ExtractsLowercaseIdAndTypeProperties()
    {
        var payload = new LowercasePayload { id = "evt-1", type = "user.created" };

        var (id, type) = TypedEventMetadataExtractor.Extract(payload, DefaultTypeInfo<LowercasePayload>());

        Assert.Multiple(() => Assert.Equal("evt-1", id), () => Assert.Equal("user.created", type));
    }

    [Fact]
    public void DoesNotMatchPascalCaseCLRNamesWithoutAttribute()
    {
        var payload = new PascalCasePayload { Id = "ignored", Type = "ignored" };

        var (id, type) = TypedEventMetadataExtractor.Extract(payload, DefaultTypeInfo<PascalCasePayload>());

        Assert.Multiple(() => Assert.Null(id), () => Assert.Null(type));
    }

    [Fact]
    public void HonorsJsonPropertyNameAttribute()
    {
        var payload = new AttributedPayload { MessageId = "evt-3", EventKind = "kind.x" };

        var (id, type) = TypedEventMetadataExtractor.Extract(payload, DefaultTypeInfo<AttributedPayload>());

        Assert.Multiple(() => Assert.Equal("evt-3", id), () => Assert.Equal("kind.x", type));
    }

    [Fact]
    public void IgnoresAttributedPropertyWhenJsonNameDoesNotMatch()
    {
        var payload = new RenamedPayload { Identifier = "wrong" };

        var (id, _) = TypedEventMetadataExtractor.Extract(payload, DefaultTypeInfo<RenamedPayload>());

        Assert.Null(id);
    }

    [Fact]
    public void ReturnsNullForMissingProperties()
    {
        var payload = new NoMetadataPayload { Content = "hello" };

        var (id, type) = TypedEventMetadataExtractor.Extract(payload, DefaultTypeInfo<NoMetadataPayload>());

        Assert.Multiple(() => Assert.Null(id), () => Assert.Null(type));
    }

    [Fact]
    public void ReturnsNullsForNullPayload()
    {
        var (id, type) = TypedEventMetadataExtractor.Extract<LowercasePayload?>(null, DefaultTypeInfo<LowercasePayload?>());

        Assert.Multiple(() => Assert.Null(id), () => Assert.Null(type));
    }

    [Fact]
    public void IgnoresNonStringIdProperty()
    {
        var payload = new NumericIdPayload { id = 42 };

        var (id, _) = TypedEventMetadataExtractor.Extract(payload, DefaultTypeInfo<NumericIdPayload>());

        Assert.Null(id);
    }

    [Fact]
    public void ReturnsNullWhenStringPropertyValueIsNull()
    {
        var payload = new LowercasePayload { id = null, type = null };

        var (id, type) = TypedEventMetadataExtractor.Extract(payload, DefaultTypeInfo<LowercasePayload>());

        Assert.Multiple(() => Assert.Null(id), () => Assert.Null(type));
    }

    [Fact]
    public void SkipsPropertiesMarkedJsonIgnore()
    {
        var payload = new IgnoredIdPayload { id = "should-be-ignored" };

        var (id, _) = TypedEventMetadataExtractor.Extract(payload, DefaultTypeInfo<IgnoredIdPayload>());

        Assert.Null(id);
    }

    [Fact]
    public void IncludesPropertiesMarkedJsonIgnoreNever()
    {
        var payload = new ConditionallyIgnoredPayload { id = "evt-9" };

        var (id, _) = TypedEventMetadataExtractor.Extract(payload, DefaultTypeInfo<ConditionallyIgnoredPayload>());

        Assert.Equal("evt-9", id);
    }

    [Fact]
    public void UsesProvidedTypeInfoNotDefaultOptions()
    {
        // A naming policy that lowercases everything: PascalCase Id → "id" on the wire.
        var opts = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        opts.MakeReadOnly();
        var typeInfo = (JsonTypeInfo<PascalCasePayload>)opts.GetTypeInfo(typeof(PascalCasePayload));

        var payload = new PascalCasePayload { Id = "evt-custom", Type = "custom.type" };
        var (id, type) = TypedEventMetadataExtractor.Extract(payload, typeInfo);

        Assert.Multiple(() => Assert.Equal("evt-custom", id), () => Assert.Equal("custom.type", type));
    }

#pragma warning disable IDE1006 // Naming Styles - exercising lowercase-property path
    private sealed record LowercasePayload
    {
        public string? id { get; init; }
        public string? type { get; init; }
    }

    private sealed record PascalCasePayload
    {
        public string? Id { get; init; }
        public string? Type { get; init; }
    }

    private sealed record AttributedPayload
    {
        [JsonPropertyName("id")]
        public string? MessageId { get; init; }

        [JsonPropertyName("type")]
        public string? EventKind { get; init; }
    }

    private sealed record RenamedPayload
    {
        [JsonPropertyName("identifier")]
        public string? Identifier { get; init; }
    }

    private sealed record NoMetadataPayload
    {
        public string? Content { get; init; }
    }

    private sealed record NumericIdPayload
    {
        public int id { get; init; }
    }

    private sealed record IgnoredIdPayload
    {
        [JsonIgnore]
        public string? id { get; init; }
    }

    private sealed record ConditionallyIgnoredPayload
    {
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public string? id { get; init; }
    }
#pragma warning restore IDE1006
}
