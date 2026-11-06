using Prosody.State;
using Prosody.Tests.TestHelpers;

namespace Prosody.Tests.Unit;

/// <summary>Verifies that value presence calls the native presence operation, not the read.</summary>
public sealed class ValuePresenceTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ValuePresenceReturnsTheNativeAnswer(bool present)
    {
        var json = new ValueState<int>(
            new FakeJsonValueStateHandle { ContainsResult = present, GetResult = present ? null : "1"u8.ToArray() },
            TestJson.TypeInfo<int>()
        );
        var message = new MessageValueState<int>(
            new FakeMessageValueStateHandle { ContainsResult = present },
            TestJson.TypeInfo<int>()
        );
        var cancellationToken = TestContext.Current.CancellationToken;

        var jsonPresent = await json.HasValueAsync(cancellationToken);
        var messagePresent = await message.HasValueAsync(cancellationToken);

        Assert.Multiple(() => Assert.Equal(present, jsonPresent), () => Assert.Equal(present, messagePresent));
    }
}
