using Prosody.Configuration;

namespace Prosody.Tests.Unit;

/// <summary>
/// Tests host values that cannot be represented by the native configuration.
/// </summary>
[Collection("Sequential")]
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

    [Fact]
    public void MissingSourceSystemAndGroupIdFails()
    {
        var sourceSystem = Environment.GetEnvironmentVariable("PROSODY_SOURCE_SYSTEM");
        var groupId = Environment.GetEnvironmentVariable("PROSODY_GROUP_ID");
        try
        {
            Environment.SetEnvironmentVariable("PROSODY_SOURCE_SYSTEM", null);
            Environment.SetEnvironmentVariable("PROSODY_GROUP_ID", null);
            var result = _validator.Validate(name: null, new ClientOptions());

            Assert.True(result.Failed);
            Assert.Contains("SourceSystem or GroupId must be set", result.FailureMessage, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PROSODY_SOURCE_SYSTEM", sourceSystem);
            Environment.SetEnvironmentVariable("PROSODY_GROUP_ID", groupId);
        }
    }

    [Fact]
    public void GroupIdSatisfiesTheSourceSystemRule()
    {
        var result = _validator.Validate(name: null, new ClientOptions { GroupId = "group" });

        Assert.True(result.Succeeded);
    }
}
