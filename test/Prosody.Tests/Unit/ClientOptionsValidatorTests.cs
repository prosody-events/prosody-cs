using Prosody.Configuration;

namespace Prosody.Tests.Unit;

/// <summary>
/// Tests the startup validation rules for the resolved shutdown timeout and source system.
/// </summary>
[Collection("Sequential")]
public sealed class ClientOptionsValidatorTests
{
    private readonly ClientOptionsValidator _validator = new();

    [Fact]
    public void ShutdownTimeoutAboveTheMaximumFails()
    {
        var options = new ClientOptions
        {
            ShutdownTimeout = ClientOptionsValidator.MaxShutdownTimeout + TimeSpan.FromTicks(1),
        };

        var result = _validator.Validate(name: null, options);

        Assert.True(result.Failed);
        Assert.Contains(
            "ShutdownTimeout, or PROSODY_SHUTDOWN_TIMEOUT, must not exceed",
            result.FailureMessage,
            StringComparison.Ordinal
        );
    }

    [Theory]
    [InlineData("2d", false)]
    [InlineData("30000y", false)]
    [InlineData("20000y 20000y", false)]
    [InlineData("1d 1ms", false)]
    [InlineData("1d", true)]
    [InlineData("0", true)]
    public void ShutdownTimeoutFromTheEnvironmentIsValidated(string text, bool valid)
    {
        var previous = Environment.GetEnvironmentVariable("PROSODY_SHUTDOWN_TIMEOUT");
        try
        {
            Environment.SetEnvironmentVariable("PROSODY_SHUTDOWN_TIMEOUT", text);

            var options = new ClientOptions { GroupId = "group" };
            var result = _validator.Validate(name: null, options);

            Assert.Equal(valid, result.Succeeded);
            if (!valid)
            {
                Assert.Contains("PROSODY_SHUTDOWN_TIMEOUT", result.FailureMessage, StringComparison.Ordinal);
                Assert.Contains("must not exceed", result.FailureMessage, StringComparison.Ordinal);
            }

            options.ShutdownTimeout = TimeSpan.FromSeconds(1);
            Assert.True(_validator.Validate(name: null, options).Succeeded);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PROSODY_SHUTDOWN_TIMEOUT", previous);
        }
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
