using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Prosody.Configuration;
using Prosody.Tests.TestHelpers;

namespace Prosody.Tests.Unit;

/// <summary>
/// Tests that <see cref="ProsodyClient"/> builds and exposes the correct <see cref="JsonSerializerOptions"/>.
/// </summary>
public sealed partial class ProsodyClientJsonOptionsTests : IDisposable
{
    private readonly ProsodyClient _client = new(
        new ClientOptions
        {
            Mock = true,
            BootstrapServers = [TestDefaults.BootstrapServers],
            GroupId = "test-group",
            SourceSystem = "test",
        }
    );

    public void Dispose() => _client.Dispose();

    [Fact]
    public void Defaults_UseCamelCaseNaming()
    {
        Assert.Equal(JsonNamingPolicy.CamelCase, _client.JsonOptions.PropertyNamingPolicy);
    }

    [Fact]
    public void Defaults_IncludeJsonStringEnumConverter()
    {
        var hasEnumConverter = _client.JsonOptions.Converters.Any(c => c is JsonStringEnumConverter);
        Assert.True(hasEnumConverter, "JsonStringEnumConverter should be present in default options");
    }

    [Fact]
    public void Defaults_WhenWritingNullIgnoreCondition()
    {
        Assert.Equal(JsonIgnoreCondition.WhenWritingNull, _client.JsonOptions.DefaultIgnoreCondition);
    }

    [Fact]
    public void ConfigureJsonSerializer_MutatorRunsAfterDefaults()
    {
        var invoked = false;
        JsonNamingPolicy? capturedPolicy = null;

        using var client = new ProsodyClient(
            new ClientOptions
            {
                Mock = true,
                BootstrapServers = [TestDefaults.BootstrapServers],
                GroupId = "test-group",
                SourceSystem = "test",
                ConfigureJsonSerializer = opts =>
                {
                    invoked = true;
                    capturedPolicy = opts.PropertyNamingPolicy;
                    opts.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
                },
            }
        );

        Assert.True(invoked, "ConfigureJsonSerializer callback should have been invoked");
        Assert.Equal(JsonNamingPolicy.CamelCase, capturedPolicy);
        Assert.Equal(JsonNamingPolicy.SnakeCaseLower, client.JsonOptions.PropertyNamingPolicy);
    }

    [Fact]
    public void NullConfigureJsonSerializer_LeavesDefaultsIntact()
    {
        using var client = new ProsodyClient(
            new ClientOptions
            {
                Mock = true,
                BootstrapServers = [TestDefaults.BootstrapServers],
                GroupId = "test-group",
                SourceSystem = "test",
                ConfigureJsonSerializer = null,
            }
        );

        Assert.Equal(JsonNamingPolicy.CamelCase, client.JsonOptions.PropertyNamingPolicy);
        Assert.Equal(JsonIgnoreCondition.WhenWritingNull, client.JsonOptions.DefaultIgnoreCondition);
    }

    [JsonSerializable(typeof(SamplePayload))]
    private sealed partial class SamplePayloadContext : JsonSerializerContext;

    private sealed record SamplePayload(string Name, int Count);

    [Fact]
    public void ConfigureJsonSerializer_CanSetTypeInfoResolver()
    {
        using var client = new ProsodyClient(
            new ClientOptions
            {
                Mock = true,
                BootstrapServers = [TestDefaults.BootstrapServers],
                GroupId = "test-group",
                SourceSystem = "test",
                ConfigureJsonSerializer = opts => opts.TypeInfoResolverChain.Add(SamplePayloadContext.Default),
            }
        );

        var typeInfo = client.JsonOptions.GetTypeInfo(typeof(SamplePayload));
        Assert.IsAssignableFrom<JsonTypeInfo<SamplePayload>>(typeInfo);
    }
}
