using Microsoft.Extensions.Options;

namespace Prosody.Tests.Unit;

/// <summary>
/// Tests for <see cref="ClientOptionsValidator"/>.
/// </summary>
public sealed class ClientOptionsValidatorTests
{
    private readonly ClientOptionsValidator _validator = new();

    public static IEnumerable<object?[]> NonFiniteDoubleValues()
    {
        yield return [double.NaN];
        yield return [double.PositiveInfinity];
        yield return [double.NegativeInfinity];
    }

    public static IEnumerable<object?[]> NonFiniteUnitIntervalCases()
    {
        foreach (var value in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
        {
            yield return [nameof(ClientOptions.DeferFailureThreshold), value];
            yield return [nameof(ClientOptions.MonopolizationThreshold), value];
            yield return [nameof(ClientOptions.SchedulerFailureWeight), value];
        }
    }

    [Fact]
    public void ValidOptionsReturnsSuccess()
    {
        var options = new ClientOptions
        {
            BootstrapServers = ["localhost:9092"],
            GroupId = "test-group",
            SubscribedTopics = ["orders"],
            Mock = true,
        };

        var result = _validator.Validate(null, options);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void DefaultOptionsReturnsSuccess()
    {
        var options = new ClientOptions();

        var result = _validator.Validate(null, options);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void EmptyBootstrapServersFails()
    {
        var options = new ClientOptions { BootstrapServers = [] };

        var result = _validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains("BootstrapServers must not be empty", result.FailureMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void EmptySubscribedTopicsFails()
    {
        var options = new ClientOptions { SubscribedTopics = [] };

        var result = _validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains("SubscribedTopics must not be empty", result.FailureMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void LowLatencyWithoutFailureTopicFails()
    {
        var options = new ClientOptions { Mode = ClientMode.LowLatency };

        var result = _validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(
            "FailureTopic is required when Mode is LowLatency",
            result.FailureMessage,
            StringComparison.Ordinal
        );
    }

    [Fact]
    public void LowLatencyWithFailureTopicSucceeds()
    {
        var options = new ClientOptions { Mode = ClientMode.LowLatency, FailureTopic = "dead-letters" };

        var result = _validator.Validate(null, options);

        Assert.True(result.Succeeded);
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(1.1)]
    public void DeferFailureThresholdOutOfRangeFails(double value)
    {
        var options = new ClientOptions { DeferFailureThreshold = value };

        var result = _validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(
            "DeferFailureThreshold must be between 0.0 and 1.0",
            result.FailureMessage,
            StringComparison.Ordinal
        );
    }

    [Theory]
    [MemberData(nameof(NonFiniteUnitIntervalCases))]
    public void NonFiniteUnitIntervalValueFails(string propertyName, double value)
    {
        var options = new ClientOptions();
        typeof(ClientOptions).GetProperty(propertyName)!.SetValue(options, value);

        var result = _validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains($"{propertyName} must be between 0.0 and 1.0", result.FailureMessage, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(1.1)]
    public void MonopolizationThresholdOutOfRangeFails(double value)
    {
        var options = new ClientOptions { MonopolizationThreshold = value };

        var result = _validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(
            "MonopolizationThreshold must be between 0.0 and 1.0",
            result.FailureMessage,
            StringComparison.Ordinal
        );
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(1.1)]
    public void SchedulerFailureWeightOutOfRangeFails(double value)
    {
        var options = new ClientOptions { SchedulerFailureWeight = value };

        var result = _validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(
            "SchedulerFailureWeight must be between 0.0 and 1.0",
            result.FailureMessage,
            StringComparison.Ordinal
        );
    }

    [Theory]
    [InlineData(nameof(ClientOptions.Timeout))]
    [InlineData(nameof(ClientOptions.StallThreshold))]
    [InlineData(nameof(ClientOptions.ShutdownTimeout))]
    [InlineData(nameof(ClientOptions.PollInterval))]
    [InlineData(nameof(ClientOptions.CommitInterval))]
    [InlineData(nameof(ClientOptions.SlabSize))]
    [InlineData(nameof(ClientOptions.SendTimeout))]
    [InlineData(nameof(ClientOptions.RetryBase))]
    [InlineData(nameof(ClientOptions.MaxRetryDelay))]
    [InlineData(nameof(ClientOptions.DeferBase))]
    [InlineData(nameof(ClientOptions.DeferMaxDelay))]
    [InlineData(nameof(ClientOptions.DeferFailureWindow))]
    [InlineData(nameof(ClientOptions.DeferSeekTimeout))]
    [InlineData(nameof(ClientOptions.MonopolizationWindow))]
    [InlineData(nameof(ClientOptions.SchedulerMaxWait))]
    [InlineData(nameof(ClientOptions.CassandraRetention))]
    public void NegativeTimeSpanFails(string propertyName)
    {
        var options = new ClientOptions();
        typeof(ClientOptions).GetProperty(propertyName)!.SetValue(options, TimeSpan.FromSeconds(-1));

        var result = _validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains($"{propertyName} must not be negative", result.FailureMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void DuplicateBootstrapServersFails()
    {
        var options = new ClientOptions { BootstrapServers = ["broker:9092", "broker:9092"] };

        var result = _validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(
            "BootstrapServers must not contain duplicates",
            result.FailureMessage,
            StringComparison.Ordinal
        );
    }

    [Fact]
    public void WhitespacePaddedDuplicateBootstrapServersFails()
    {
        var options = new ClientOptions { BootstrapServers = ["broker:9092", " broker:9092 "] };

        var result = _validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Multiple(
            () =>
                Assert.Contains(
                    "BootstrapServers[1] must not contain leading or trailing whitespace",
                    result.FailureMessage,
                    StringComparison.Ordinal
                ),
            () =>
                Assert.Contains(
                    "BootstrapServers must not contain duplicates",
                    result.FailureMessage,
                    StringComparison.Ordinal
                )
        );
    }

    [Fact]
    public void DuplicateSubscribedTopicsFails()
    {
        var options = new ClientOptions { SubscribedTopics = ["orders", "orders"] };

        var result = _validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(
            "SubscribedTopics must not contain duplicates",
            result.FailureMessage,
            StringComparison.Ordinal
        );
    }

    [Fact]
    public void DuplicateAllowedEventsFails()
    {
        var options = new ClientOptions { AllowedEvents = ["user.", "user."] };

        var result = _validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains("AllowedEvents must not contain duplicates", result.FailureMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void DuplicateCassandraNodesFails()
    {
        var options = new ClientOptions { CassandraNodes = ["cass:9042", "cass:9042"] };

        var result = _validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains("CassandraNodes must not contain duplicates", result.FailureMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void WhitespaceBootstrapServerEntryFails()
    {
        var options = new ClientOptions { BootstrapServers = ["localhost:9092", "", "  "] };

        var result = _validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Multiple(
            () =>
                Assert.Contains(
                    "BootstrapServers[1] must not be empty or whitespace",
                    result.FailureMessage,
                    StringComparison.Ordinal
                ),
            () =>
                Assert.Contains(
                    "BootstrapServers[2] must not be empty or whitespace",
                    result.FailureMessage,
                    StringComparison.Ordinal
                )
        );
    }

    [Fact]
    public void WhitespaceSubscribedTopicEntryFails()
    {
        var options = new ClientOptions { SubscribedTopics = ["", "orders"] };

        var result = _validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(
            "SubscribedTopics[0] must not be empty or whitespace",
            result.FailureMessage,
            StringComparison.Ordinal
        );
    }

    [Fact]
    public void WhitespaceCassandraNodeEntryFails()
    {
        var options = new ClientOptions { CassandraNodes = ["cass:9042", " "] };

        var result = _validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(
            "CassandraNodes[1] must not be empty or whitespace",
            result.FailureMessage,
            StringComparison.Ordinal
        );
    }

    [Fact]
    public void WhitespaceAllowedEventEntryFails()
    {
        var options = new ClientOptions { AllowedEvents = ["user.", ""] };

        var result = _validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(
            "AllowedEvents[1] must not be empty or whitespace",
            result.FailureMessage,
            StringComparison.Ordinal
        );
    }

    [Fact]
    public void NegativeSchedulerWaitWeightFails()
    {
        var options = new ClientOptions { SchedulerWaitWeight = -1.0 };

        var result = _validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains("SchedulerWaitWeight must not be negative", result.FailureMessage, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(NonFiniteDoubleValues))]
    public void NonFiniteSchedulerWaitWeightFails(double value)
    {
        var options = new ClientOptions { SchedulerWaitWeight = value };

        var result = _validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains("SchedulerWaitWeight must be finite", result.FailureMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void PositiveSchedulerWaitWeightSucceeds()
    {
        var options = new ClientOptions { SchedulerWaitWeight = 200.0 };

        var result = _validator.Validate(null, options);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void MultipleFailuresAreReportedTogether()
    {
        var options = new ClientOptions
        {
            BootstrapServers = [],
            SubscribedTopics = [],
            DeferFailureThreshold = 2.0,
            Timeout = TimeSpan.FromSeconds(-1),
        };

        var result = _validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Multiple(
            () =>
                Assert.Contains("BootstrapServers must not be empty", result.FailureMessage, StringComparison.Ordinal),
            () =>
                Assert.Contains("SubscribedTopics must not be empty", result.FailureMessage, StringComparison.Ordinal),
            () =>
                Assert.Contains(
                    "DeferFailureThreshold must be between 0.0 and 1.0",
                    result.FailureMessage,
                    StringComparison.Ordinal
                ),
            () => Assert.Contains("Timeout must not be negative", result.FailureMessage, StringComparison.Ordinal)
        );
    }

    [Fact]
    public void CaseInsensitiveDuplicateDetection()
    {
        var options = new ClientOptions { BootstrapServers = ["Broker:9092", "broker:9092"] };

        var result = _validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(
            "BootstrapServers must not contain duplicates",
            result.FailureMessage,
            StringComparison.Ordinal
        );
    }
}
