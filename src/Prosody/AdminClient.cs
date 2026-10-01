using Prosody.Errors;
using Prosody.Infrastructure;

namespace Prosody;

/// <summary>
/// Client for Kafka administrative operations, used primarily for integration testing.
/// </summary>
public sealed class AdminClient : IDisposable
{
    private readonly Native.AdminClient _native;
    private bool _disposed;

    /// <summary>
    /// Creates a new AdminClient with the specified bootstrap servers.
    /// </summary>
    /// <param name="bootstrapServers">Kafka bootstrap servers to connect to.</param>
    /// <exception cref="ArgumentException"><paramref name="bootstrapServers"/> is not a valid server list.</exception>
    /// <exception cref="ProsodyException">The admin client cannot start.</exception>
    public AdminClient(params string[] bootstrapServers)
    {
        _native = NativeErrors.Run(() => new Native.AdminClient(bootstrapServers));
    }

    /// <summary>
    /// Creates a new Kafka topic.
    /// </summary>
    /// <param name="name">The name of the topic to create.</param>
    /// <param name="partitionCount">Number of partitions for the topic.</param>
    /// <param name="replicationFactor">Replication factor for the topic.</param>
    /// <param name="cleanupPolicy">
    /// The <c>cleanup.policy</c> of the topic, such as <c>delete</c>, <c>compact</c>, or
    /// <c>delete,compact</c>. <see langword="null"/> keeps the cluster default.
    /// </param>
    /// <param name="retention">
    /// How long the topic keeps a message. <see langword="null"/> keeps the cluster default.
    /// </param>
    /// <exception cref="ArgumentException">The topic configuration is invalid.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="retention"/> is negative.</exception>
    /// <exception cref="ProsodyException">The broker rejected the topic, or it did not become ready.</exception>
    public Task CreateTopicAsync(
        string name,
        ushort partitionCount,
        ushort replicationFactor,
        string? cleanupPolicy = null,
        TimeSpan? retention = null
    )
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var nativeRetention = Durations.ToNative(retention);
        return NativeErrors.RunAsync(() =>
            _native.CreateTopic(name, partitionCount, replicationFactor, cleanupPolicy, nativeRetention)
        );
    }

    /// <summary>
    /// Creates a new Kafka topic with the cluster default cleanup policy and retention.
    /// </summary>
    /// <inheritdoc cref="CreateTopicAsync(string, ushort, ushort, string?, TimeSpan?)"/>
    public Task CreateTopicAsync(string name, ushort partitionCount, ushort replicationFactor) =>
        CreateTopicAsync(name, partitionCount, replicationFactor, null, null);

    /// <summary>
    /// Deletes a Kafka topic.
    /// </summary>
    /// <param name="name">The name of the topic to delete.</param>
    /// <exception cref="ProsodyException">The broker did not delete the topic.</exception>
    public Task DeleteTopicAsync(string name)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return NativeErrors.RunAsync(() => _native.DeleteTopic(name));
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _native.Dispose();
    }
}
