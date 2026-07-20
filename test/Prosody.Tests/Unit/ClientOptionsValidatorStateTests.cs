using Prosody.Configuration;
using Prosody.State;

namespace Prosody.Tests.Unit;

/// <summary>
/// Tests for keyed-state set-level and cross-field validation in <see cref="ClientOptionsValidator"/>.
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
    public void DuplicateStateNames_Fails()
    {
        var options = BaseOptions();
        options.StateCollections = [StateDefinition.Value<int>("dup"), StateDefinition.Map<int>("dup")];

        var result = _validator.Validate(name: null, options);

        Assert.Multiple(
            () => Assert.True(result.Failed),
            () => Assert.Contains("duplicate", result.FailureMessage, StringComparison.OrdinalIgnoreCase)
        );
    }

    [Fact]
    public void UniqueStateNames_Succeeds()
    {
        var options = BaseOptions();
        options.StateCollections = [StateDefinition.Value<int>("a"), StateDefinition.Map<int>("b")];

        var result = _validator.Validate(name: null, options);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void StateCacheDir_Empty_Fails()
    {
        var options = BaseOptions();
        options.StateCacheDir = "";

        var result = _validator.Validate(name: null, options);

        Assert.Multiple(
            () => Assert.True(result.Failed),
            () => Assert.Contains("StateCacheDir", result.FailureMessage, StringComparison.Ordinal)
        );
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void StateCacheSizeBytes_NonPositive_Fails(long bytes)
    {
        var options = BaseOptions();
        options.StateCacheSizeBytes = bytes;

        var result = _validator.Validate(name: null, options);

        Assert.Multiple(
            () => Assert.True(result.Failed),
            () => Assert.Contains("StateCacheSizeBytes", result.FailureMessage, StringComparison.Ordinal)
        );
    }

    [Fact]
    public void RecoveryDelay_Fractional_Fails()
    {
        var options = BaseOptions();
        options.StateRecoveryDelay = TimeSpan.FromMilliseconds(1500);

        var result = _validator.Validate(name: null, options);

        Assert.Multiple(
            () => Assert.True(result.Failed),
            () => Assert.Contains("StateRecoveryDelay", result.FailureMessage, StringComparison.Ordinal)
        );
    }

    [Fact]
    public void RecoveryDelay_Zero_Fails()
    {
        var options = BaseOptions();
        options.StateRecoveryDelay = TimeSpan.Zero;

        var result = _validator.Validate(name: null, options);

        Assert.True(result.Failed);
    }

    [Fact]
    public void RecoveryDelay_WholeSeconds_Succeeds()
    {
        var options = BaseOptions();
        options.StateRecoveryDelay = TimeSpan.FromSeconds(30);

        var result = _validator.Validate(name: null, options);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Ttl_NotExceedingRecoveryDelay_Fails()
    {
        var options = BaseOptions();
        options.StateRecoveryDelay = TimeSpan.FromSeconds(5);
        options.StateCollections = [StateDefinition.Value<int>("v", ttl: TimeSpan.FromSeconds(5))];

        var result = _validator.Validate(name: null, options);

        Assert.Multiple(
            () => Assert.True(result.Failed),
            () => Assert.Contains("must exceed StateRecoveryDelay", result.FailureMessage, StringComparison.Ordinal)
        );
    }

    [Fact]
    public void Ttl_ExceedingRecoveryDelay_Succeeds()
    {
        var options = BaseOptions();
        options.StateRecoveryDelay = TimeSpan.FromSeconds(5);
        options.StateCollections = [StateDefinition.Value<int>("v", ttl: TimeSpan.FromSeconds(6))];

        var result = _validator.Validate(name: null, options);

        Assert.True(result.Succeeded);
    }
}
