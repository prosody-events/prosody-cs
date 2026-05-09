using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Prosody.Configuration;
using Prosody.Infrastructure;
using Prosody.Messaging;
using Prosody.Tests.TestHelpers;

namespace Prosody.Tests.Unit;

// Namespace-level so the STJ source generator can reference them from its .g.cs output
internal sealed record JsonOptionsTestPayload(string Name, int Count);

[JsonSerializable(typeof(JsonOptionsTestPayload))]
internal sealed partial class JsonOptionsTestPayloadContext : JsonSerializerContext;

/// <summary>
/// Tests that <see cref="ProsodyClient"/> builds and exposes the correct <see cref="JsonSerializerOptions"/>.
/// </summary>
public sealed class ProsodyClientJsonOptionsTests : IDisposable
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
    public void ConfigureJsonOptions_MutatorRunsAfterDefaults()
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
                ConfigureJsonOptions = opts =>
                {
                    invoked = true;
                    capturedPolicy = opts.PropertyNamingPolicy;
                    opts.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
                },
            }
        );

        Assert.True(invoked, "ConfigureJsonOptions callback should have been invoked");
        Assert.Equal(JsonNamingPolicy.CamelCase, capturedPolicy);
        Assert.Equal(JsonNamingPolicy.SnakeCaseLower, client.JsonOptions.PropertyNamingPolicy);
    }

    [Fact]
    public void NullConfigureJsonOptions_LeavesDefaultsIntact()
    {
        using var client = new ProsodyClient(
            new ClientOptions
            {
                Mock = true,
                BootstrapServers = [TestDefaults.BootstrapServers],
                GroupId = "test-group",
                SourceSystem = "test",
                ConfigureJsonOptions = null,
            }
        );

        Assert.Equal(JsonNamingPolicy.CamelCase, client.JsonOptions.PropertyNamingPolicy);
        Assert.Equal(JsonIgnoreCondition.WhenWritingNull, client.JsonOptions.DefaultIgnoreCondition);
    }

    [Fact]
    public void JsonOptions_IsReadOnly_AfterConstruction()
    {
        Assert.Throws<InvalidOperationException>(() =>
            _client.JsonOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
        );
    }

    [Fact]
    public void ConfigureJsonOptions_SecondBuilderCallWinsOverFirst()
    {
        // Two consecutive .ConfigureJsonOptions calls on the builder: the second assignment overwrites the first
        // because ConfigureJsonOptions is a simple property, not a chain/accumulator.
        using var client = ProsodyClientBuilder
            .Create()
            .WithBootstrapServers(TestDefaults.BootstrapServers)
            .WithGroupId("test-group")
            .WithSourceSystem("test")
            .WithMock(true)
            .ConfigureJsonOptions(_ => { })
            .ConfigureJsonOptions(opts => opts.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower)
            .Build();

        Assert.Equal(JsonNamingPolicy.SnakeCaseLower, client.JsonOptions.PropertyNamingPolicy);
    }

    [Fact]
    public void ConfigureJsonOptions_CanSetTypeInfoResolver()
    {
        using var client = new ProsodyClient(
            new ClientOptions
            {
                Mock = true,
                BootstrapServers = [TestDefaults.BootstrapServers],
                GroupId = "test-group",
                SourceSystem = "test",
                ConfigureJsonOptions = opts => opts.TypeInfoResolverChain.Add(JsonOptionsTestPayloadContext.Default),
            }
        );

        var typeInfo = client.JsonOptions.GetTypeInfo(typeof(JsonOptionsTestPayload));
        Assert.IsAssignableFrom<JsonTypeInfo<JsonOptionsTestPayload>>(typeInfo);
    }

    private static readonly JsonSerializerOptions SnakeCaseOptions = BuildSnakeCaseOptions();

    private static JsonSerializerOptions BuildSnakeCaseOptions()
    {
        var o = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
        };
        o.MakeReadOnly();
        return o;
    }

    [Fact]
    public async Task ConfigureJsonOptions_SubscribeUsesConfiguredOptions()
    {
        // Build a client with snake_case naming policy; drive bytes through the bridge,
        // assert the handler observes the deserialized payload using snake_case keys.
        var received = false;
        RecordWithSnakeCaseField? payload = null;

        await using var client = new ProsodyClient(
            new ClientOptions
            {
                Mock = true,
                BootstrapServers = [TestDefaults.BootstrapServers],
                GroupId = "test-group",
                SourceSystem = "test",
                ConfigureJsonOptions = opts => opts.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            }
        );

        var handler = new LambdaHandler<RecordWithSnakeCaseField>(
            onMessage: (_, msg, _) =>
            {
                received = true;
                payload = msg.Payload;
                return Task.CompletedTask;
            }
        );

        // Serialize using snake_case and drive through the bridge directly (no FFI needed)
        var jsonBytes = JsonSerializer.SerializeToUtf8Bytes(new RecordWithSnakeCaseField("hello"), SnakeCaseOptions);

        var bridge = new EventHandlerBridge<RecordWithSnakeCaseField>(handler, client.JsonOptions);

        await bridge.HandleMessageAsync(
            new ProsodyContext(),
            "t",
            "k",
            0,
            0L,
            default,
            jsonBytes,
            TestDefaults.NeverCancel,
            TestDefaults.EmptyCarrier
        );

        Assert.True(received);
        Assert.Equal("hello", payload?.FieldValue);
    }

    private sealed record RecordWithSnakeCaseField(string FieldValue);

    private sealed class LambdaHandler<T>(Func<ProsodyContext, Message<T>, CancellationToken, Task>? onMessage = null)
        : IProsodyHandler<T>
    {
        public Task OnMessageAsync(
            ProsodyContext prosodyContext,
            Message<T> message,
            CancellationToken cancellationToken
        ) => onMessage?.Invoke(prosodyContext, message, cancellationToken) ?? Task.CompletedTask;

        public Task OnTimerAsync(
            ProsodyContext prosodyContext,
            ProsodyTimer timer,
            CancellationToken cancellationToken
        ) => Task.CompletedTask;
    }
}
