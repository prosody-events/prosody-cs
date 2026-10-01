using Prosody.Configuration;

namespace Prosody.Tests.Unit;

/// <summary>
/// Tests the startup validation rule for the resolved source system.
/// </summary>
[Collection("Sequential")]
public sealed class ClientOptionsValidatorTests
{
    private readonly ClientOptionsValidator _validator = new();

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
