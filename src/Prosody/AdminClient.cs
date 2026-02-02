namespace Prosody;

/// <summary>
/// Client for Kafka administrative operations.
/// </summary>
/// <remarks>
/// Used primarily for integration testing to create and delete test topics.
/// </remarks>
public sealed class AdminClient : IDisposable
{
    private readonly Native.AdminClient _native;
    private bool _disposed;

    /// <summary>
    /// Creates a new AdminClient with the specified bootstrap servers.
    /// </summary>
    /// <param name="bootstrapServers">Kafka bootstrap servers to connect to.</param>
    public AdminClient(params string[] bootstrapServers)
    {
        _native = new Native.AdminClient(bootstrapServers);
    }

    /// <summary>
    /// Creates a new Kafka topic.
    /// </summary>
    /// <param name="name">The name of the topic to create.</param>
    /// <param name="partitionCount">Number of partitions for the topic.</param>
    /// <param name="replicationFactor">Replication factor for the topic.</param>
    public Task CreateTopicAsync(string name, ushort partitionCount, ushort replicationFactor)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _native.CreateTopic(name, partitionCount, replicationFactor);
    }

    /// <summary>
    /// Deletes a Kafka topic.
    /// </summary>
    /// <param name="name">The name of the topic to delete.</param>
    public Task DeleteTopicAsync(string name)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _native.DeleteTopic(name);
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
