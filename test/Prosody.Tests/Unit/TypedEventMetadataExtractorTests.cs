using System.Text.Json.Serialization;
using Prosody.Infrastructure;

namespace Prosody.Tests.Unit;

/// <summary>
/// Tests for <see cref="TypedEventMetadataExtractor{T}"/>: id/type extraction from
/// typed payloads via reflection, with no JSON parsing.
/// </summary>
public sealed class TypedEventMetadataExtractorTests
{
    [Fact]
    public void ExtractsLowercaseIdAndTypeProperties()
    {
        var payload = new LowercasePayload { id = "evt-1", type = "user.created" };

        var (id, type) = TypedEventMetadataExtractor<LowercasePayload>.Extract(payload);

        Assert.Multiple(() => Assert.Equal("evt-1", id), () => Assert.Equal("user.created", type));
    }

    [Fact]
    public void ExtractsPascalCaseIdAndTypeProperties()
    {
        var payload = new PascalCasePayload { Id = "evt-2", Type = "order.placed" };

        var (id, type) = TypedEventMetadataExtractor<PascalCasePayload>.Extract(payload);

        Assert.Multiple(() => Assert.Equal("evt-2", id), () => Assert.Equal("order.placed", type));
    }

    [Fact]
    public void HonorsJsonPropertyNameAttribute()
    {
        var payload = new AttributedPayload { MessageId = "evt-3", EventKind = "kind.x" };

        var (id, type) = TypedEventMetadataExtractor<AttributedPayload>.Extract(payload);

        Assert.Multiple(() => Assert.Equal("evt-3", id), () => Assert.Equal("kind.x", type));
    }

    [Fact]
    public void IgnoresAttributedPropertyWhenJsonNameDoesNotMatch()
    {
        var payload = new RenamedPayload { Identifier = "wrong" };

        var (id, _) = TypedEventMetadataExtractor<RenamedPayload>.Extract(payload);

        Assert.Null(id);
    }

    [Fact]
    public void ReturnsNullForMissingProperties()
    {
        var payload = new NoMetadataPayload { Content = "hello" };

        var (id, type) = TypedEventMetadataExtractor<NoMetadataPayload>.Extract(payload);

        Assert.Multiple(() => Assert.Null(id), () => Assert.Null(type));
    }

    [Fact]
    public void ReturnsNullsForNullPayload()
    {
        var (id, type) = TypedEventMetadataExtractor<PascalCasePayload?>.Extract(null);

        Assert.Multiple(() => Assert.Null(id), () => Assert.Null(type));
    }

    [Fact]
    public void IgnoresNonStringIdProperty()
    {
        var payload = new NumericIdPayload { Id = 42 };

        var (id, _) = TypedEventMetadataExtractor<NumericIdPayload>.Extract(payload);

        Assert.Null(id);
    }

    [Fact]
    public void ReturnsNullWhenStringPropertyValueIsNull()
    {
        var payload = new PascalCasePayload { Id = null!, Type = null! };

        var (id, type) = TypedEventMetadataExtractor<PascalCasePayload>.Extract(payload);

        Assert.Multiple(() => Assert.Null(id), () => Assert.Null(type));
    }

    [Fact]
    public void SkipsPropertiesMarkedJsonIgnore()
    {
        var payload = new IgnoredIdPayload { Id = "should-be-ignored" };

        var (id, _) = TypedEventMetadataExtractor<IgnoredIdPayload>.Extract(payload);

        Assert.Null(id);
    }

    [Fact]
    public void IncludesPropertiesMarkedJsonIgnoreNever()
    {
        var payload = new ConditionallyIgnoredPayload { Id = "evt-9" };

        var (id, _) = TypedEventMetadataExtractor<ConditionallyIgnoredPayload>.Extract(payload);

        Assert.Equal("evt-9", id);
    }

    [Fact]
    public void AttributeMatchTakesPrecedenceOverNameMatch()
    {
        var payload = new MixedPrecedencePayload { Id = "by-name", MessageId = "by-attribute" };

        var (id, _) = TypedEventMetadataExtractor<MixedPrecedencePayload>.Extract(payload);

        Assert.Equal("by-attribute", id);
    }

    private sealed record LowercasePayload
    {
#pragma warning disable IDE1006 // Naming Styles - exercising lowercase-property path
        public string? id { get; init; }
        public string? type { get; init; }
#pragma warning restore IDE1006
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
        public int Id { get; init; }
    }

    private sealed record IgnoredIdPayload
    {
        [JsonIgnore]
        public string? Id { get; init; }
    }

    private sealed record ConditionallyIgnoredPayload
    {
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public string? Id { get; init; }
    }

    private sealed record MixedPrecedencePayload
    {
        public string? Id { get; init; }

        [JsonPropertyName("id")]
        public string? MessageId { get; init; }
    }
}
