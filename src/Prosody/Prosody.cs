using System.Diagnostics.CodeAnalysis;

namespace Prosody;

/// <summary>
/// Convenience entry point for creating Prosody clients.
/// </summary>
/// <example>
/// <code>
/// await using var client = Prosody.CreateClient()
///     .WithBootstrapServers("localhost:9092")
///     .WithGroupId("my-app")
///     .WithSubscribedTopics("my-topic")
///     .Build();
/// </code>
/// </example>
[SuppressMessage(
    "Naming",
    "CA1724:Type names should not match namespaces",
    Justification = "Prosody.CreateClient() is an alternative to ProsodyClientBuilder.Create() and is more discoverable"
)]
[SuppressMessage(
    "Design",
    "MA0049:Type name should not match containing namespace",
    Justification = "Prosody.CreateClient() is an alternative to ProsodyClientBuilder.Create() and is more discoverable"
)]
public static class Prosody
{
    /// <summary>
    /// Creates a new builder for configuring a <see cref="ProsodyClient"/>.
    /// </summary>
    /// <returns>A new <see cref="ProsodyClientBuilder"/> instance.</returns>
    public static ProsodyClientBuilder CreateClient() => ProsodyClientBuilder.Create();
}
