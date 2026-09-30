using System.Text.Json;
using Prosody.Configuration;
using Prosody.Errors;
using Prosody.Infrastructure;
using Prosody.State;
using Prosody.Tests.TestHelpers;

namespace Prosody.Tests.Unit;

/// <summary>
/// Tests that no public call lets a native exception escape, and that translation keeps the
/// category the handler bridge reads.
/// </summary>
public sealed class NativeErrorTests
{
    private static ClientOptions MockOptions =>
        new()
        {
            Mock = true,
            BootstrapServers = [TestDefaults.BootstrapServers],
            GroupId = "native-error-tests",
            SubscribedTopics = ["native-error-tests"],
        };

    public static TheoryData<Type> Variants() =>
        [.. typeof(Native.FfiException).GetNestedTypes().Where(type => type.IsSubclassOf(typeof(Native.FfiException)))];

    [Theory]
    [MemberData(nameof(Variants))]
    public void TranslateHidesEveryVariantAndKeepsTheCategory(Type variant)
    {
        var error = (Native.FfiException)Activator.CreateInstance(variant, "boom")!;

        var translated = NativeErrors.Translate(error);

        Assert.Multiple(
            () => Assert.IsNotAssignableFrom<Native.FfiException>(translated),
            () => Assert.Equal(error is Native.FfiException.PermanentState, translated is IPermanentError),
            () => Assert.Contains("boom", translated.Message, StringComparison.Ordinal)
        );
    }

    [Theory]
    [InlineData(typeof(Native.FfiException.InvalidArgument), typeof(ArgumentException))]
    [InlineData(typeof(Native.FfiException.CompactDateTime), typeof(ArgumentOutOfRangeException))]
    [InlineData(typeof(Native.FfiException.InvalidOperation), typeof(InvalidOperationException))]
    [InlineData(typeof(Native.FfiException.ConsumerConfiguration), typeof(InvalidOperationException))]
    [InlineData(typeof(Native.FfiException.PermanentState), typeof(PermanentStateException))]
    [InlineData(typeof(Native.FfiException.TransientState), typeof(TransientStateException))]
    [InlineData(typeof(Native.FfiException.Client), typeof(ProsodyException))]
    [InlineData(typeof(Native.FfiException.Producer), typeof(ProsodyException))]
    public void TranslateMapsCallerMistakesToStandardTypes(Type variant, Type expected)
    {
        var error = (Native.FfiException)Activator.CreateInstance(variant, "boom")!;

        Assert.IsType(expected, NativeErrors.Translate(error), exactMatch: true);
    }

    [Fact]
    public async Task LifecycleMistakesThrowInvalidOperationException()
    {
        var invalidSize = MockOptions;
        invalidSize.StateOwnedCacheSize = "lots";
        await using var client = await ProsodyClient.CreateAsync(MockOptions);
        await client.SubscribeAsync(new LambdaHandler<JsonElement>());

        await Assert.ThrowsAsync<InvalidOperationException>(() => ProsodyClient.CreateAsync(invalidSize));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.SubscribeAsync(new LambdaHandler<JsonElement>())
        );
        await client.UnsubscribeAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(client.UnsubscribeAsync);
    }

    [Fact]
    public async Task PublishedReaderOpenKeepsTheCategory()
    {
        await using var client = await ProsodyClient.CreateAsync(MockOptions);
        await using var unconfigured = await ProsodyClient.CreateAsync(
            new ClientOptions
            {
                Mock = true,
                BootstrapServers = [TestDefaults.BootstrapServers],
                SourceSystem = "reader",
            }
        );
        var definition = StateDefinition.Value<int>("current", published: true);
        var cancellationToken = TestContext.Current.CancellationToken;

        await Assert.ThrowsAsync<ArgumentException>(() => client.StateAsync("", definition, cancellationToken));
        await Assert.ThrowsAsync<TransientStateException>(() =>
            unconfigured.StateAsync("orders", definition, cancellationToken)
        );
    }
}
