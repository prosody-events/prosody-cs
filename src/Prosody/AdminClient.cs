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
    /// <exception cref="ArgumentException">The topic configuration is invalid.</exception>
    /// <exception cref="ProsodyException">The broker rejected the topic, or it did not become ready.</exception>
    public Task CreateTopicAsync(string name, ushort partitionCount, ushort replicationFactor)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return NativeErrors.RunAsync(() => _native.CreateTopic(name, partitionCount, replicationFactor));
    }

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
