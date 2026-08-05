using Prosody.Configuration;

namespace Prosody.Tests.Unit;

/// <summary>
/// Tests host values that cannot be represented by the native configuration.
/// </summary>
public sealed class ClientOptionsValidatorTests
{
    private readonly ClientOptionsValidator _validator = new();

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
    [InlineData(nameof(ClientOptions.LoaderSeekTimeout))]
    [InlineData(nameof(ClientOptions.MonopolizationWindow))]
    [InlineData(nameof(ClientOptions.SchedulerMaxWait))]
    [InlineData(nameof(ClientOptions.CassandraRetention))]
    public void NegativeTimeSpanFails(string propertyName)
    {
        var options = new ClientOptions();
        typeof(ClientOptions).GetProperty(propertyName)!.SetValue(options, TimeSpan.FromSeconds(-1));

        var result = _validator.Validate(name: null, options);

        Assert.True(result.Failed);
        Assert.Contains($"{propertyName} must not be negative", result.FailureMessage, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0u)]
    [InlineData(10_001u)]
    public void MaxConcurrencyOutsidePoolCapacityFails(uint maxConcurrency)
    {
        var options = new ClientOptions { MaxConcurrency = maxConcurrency };

        var result = _validator.Validate(name: null, options);

        Assert.True(result.Failed);
        Assert.Contains("MaxConcurrency must be between 1 and 10000", result.FailureMessage, StringComparison.Ordinal);
    }
}
