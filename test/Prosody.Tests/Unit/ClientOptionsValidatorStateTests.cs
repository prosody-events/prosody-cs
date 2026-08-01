using Prosody.Configuration;
using Prosody.State;

namespace Prosody.Tests.Unit;

/// <summary>
/// Tests host values that cannot be mapped into keyed-state definitions.
/// </summary>
public sealed class ClientOptionsValidatorStateTests
{
    private readonly ClientOptionsValidator _validator = new();

    private static ClientOptions BaseOptions() =>
        new()
        {
            BootstrapServers = ["localhost:9092"],
            GroupId = "test-group",
            SubscribedTopics = ["orders"],
            Mock = true,
        };

    [Fact]
    public void NullStateCollection_Fails()
    {
        var options = BaseOptions();
        options.StateCollections = [null!];

        var result = _validator.Validate(name: null, options);

        Assert.Multiple(
            () => Assert.True(result.Failed),
            () => Assert.Contains("StateCollections[0]", result.FailureMessage, StringComparison.Ordinal)
        );
    }
}
