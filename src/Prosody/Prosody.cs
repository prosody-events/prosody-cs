namespace Prosody;

/// <summary>
/// Factory for creating Prosody clients using the builder pattern.
/// </summary>
/// <example>
/// <code>
/// // Simple usage - chain directly to Build()
/// await using var client = Prosody.CreateClient()
///     .WithBootstrapServers("localhost:9092")
///     .WithGroupId("my-app")
///     .WithSubscribedTopics("my-topic")
///     .Build();
///
/// // Or store the builder for conditional configuration
/// var builder = Prosody.CreateClient()
///     .WithBootstrapServers("localhost:9092")
///     .WithGroupId("my-app");
///
/// if (isDevelopment)
///     builder = builder.WithMock(true);
///
/// await using var client = builder.Build();
/// </code>
/// </example>
public static class Prosody
{
    /// <summary>
    /// Creates a new builder for configuring a <see cref="ProsodyClient"/>.
    /// </summary>
    /// <returns>A new <see cref="ProsodyClientBuilder"/> instance.</returns>
    public static ProsodyClientBuilder CreateClient() => new();
}
