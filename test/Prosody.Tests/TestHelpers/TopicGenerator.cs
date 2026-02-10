namespace Prosody.Tests.TestHelpers;

/// <summary>
/// Generates unique topic, group, and key names for test isolation.
/// </summary>
internal static class TopicGenerator
{
    /// <summary>
    /// Generates a unique topic name (max 40 chars).
    /// </summary>
    public static string GenerateTopicName()
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var guid = Guid.NewGuid().ToString("N");
        var name = $"test-topic-{timestamp}-{guid}";
        return name.Length > 40 ? name[..40] : name;
    }

    /// <summary>
    /// Generates a unique consumer group ID (max 40 chars).
    /// </summary>
    public static string GenerateGroupId()
    {
        var guid = Guid.NewGuid().ToString("N");
        var name = $"test-group-{guid}";
        return name.Length > 40 ? name[..40] : name;
    }

    /// <summary>
    /// Generates a unique message key.
    /// </summary>
    public static string GenerateKey()
    {
        return $"test-key-{Guid.NewGuid():N}";
    }
}
