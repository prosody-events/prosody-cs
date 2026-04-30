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
    public void DoesNotMatchPascalCaseCLRNamesWithoutAttribute()
    {
        var payload = new PascalCasePayload { Id = "ignored", Type = "ignored" };

        var (id, type) = TypedEventMetadataExtractor<PascalCasePayload>.Extract(payload);

        Assert.Multiple(() => Assert.Null(id), () => Assert.Null(type));
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
        var (id, type) = TypedEventMetadataExtractor<LowercasePayload?>.Extract(null);

        Assert.Multiple(() => Assert.Null(id), () => Assert.Null(type));
    }

    [Fact]
    public void IgnoresNonStringIdProperty()
    {
        var payload = new NumericIdPayload { id = 42 };

        var (id, _) = TypedEventMetadataExtractor<NumericIdPayload>.Extract(payload);

        Assert.Null(id);
    }

    [Fact]
    public void ReturnsNullWhenStringPropertyValueIsNull()
    {
        var payload = new LowercasePayload { id = null, type = null };

        var (id, type) = TypedEventMetadataExtractor<LowercasePayload>.Extract(payload);

        Assert.Multiple(() => Assert.Null(id), () => Assert.Null(type));
    }

    [Fact]
    public void SkipsPropertiesMarkedJsonIgnore()
    {
        var payload = new IgnoredIdPayload { id = "should-be-ignored" };

        var (id, _) = TypedEventMetadataExtractor<IgnoredIdPayload>.Extract(payload);

        Assert.Null(id);
    }

    [Fact]
    public void IncludesPropertiesMarkedJsonIgnoreNever()
    {
        var payload = new ConditionallyIgnoredPayload { id = "evt-9" };

        var (id, _) = TypedEventMetadataExtractor<ConditionallyIgnoredPayload>.Extract(payload);

        Assert.Equal("evt-9", id);
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
