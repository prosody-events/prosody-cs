namespace Prosody.Tests.TestHelpers;

/// <summary>
/// Generates unique topic and group names for test isolation.
/// Each test gets its own topic/group to prevent interference.
/// </summary>
/// <remarks>
/// Reference: prosody-rb uses SecureRandom.hex(4), prosody-py uses uuid.uuid4().hex
/// </remarks>
public static class TopicGenerator
{
    /// <summary>
    /// Generates a unique topic name for a test.
    /// Format: test-topic-{timestamp}-{guid} truncated to 40 chars (Kafka limit is 249).
    /// </summary>
    /// <returns>A unique topic name.</returns>
    public static string GenerateTopicName()
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var guid = Guid.NewGuid().ToString("N");
        var name = $"test-topic-{timestamp}-{guid}";
        return name.Length > 40 ? name[..40] : name;
    }

    /// <summary>
    /// Generates a unique consumer group ID for a test.
    /// Format: test-group-{guid} truncated to 40 chars.
    /// </summary>
    /// <returns>A unique group ID.</returns>
    public static string GenerateGroupId()
    {
        var guid = Guid.NewGuid().ToString("N");
        var name = $"test-group-{guid}";
        return name.Length > 40 ? name[..40] : name;
    }

    /// <summary>
    /// Generates a unique key for test messages.
    /// Format: test-key-{guid}
    /// </summary>
    /// <returns>A unique message key.</returns>
    public static string GenerateKey()
    {
        return $"test-key-{Guid.NewGuid():N}";
    }
}
