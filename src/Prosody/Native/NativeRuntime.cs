namespace Prosody.Native;

/// <summary>
/// Manages the lifecycle of the native Prosody runtime.
/// </summary>
internal sealed class NativeRuntime
{
    /// <summary>
    /// Gets the singleton instance of the runtime.
    /// </summary>
    public static NativeRuntime Instance { get; } = new();

    private readonly object _lock = new();
    internal int _refCount;
    private ProsodyRuntimeSafeHandle? _handle;
    private int? _workerThreads;

    private NativeRuntime()
    {
    }

    /// <summary>
    /// Sets the number of worker threads for the runtime.
    /// </summary>
    /// <param name="workerThreads">The number of worker threads, or null for default.</param>
    public void SetWorkerThreads(int? workerThreads)
    {
        _workerThreads = workerThreads;
    }

    /// <summary>
    /// Acquires a reference to the runtime, initializing it if necessary.
    /// </summary>
    /// <returns>A safe handle to the runtime.</returns>
    public ProsodyRuntimeSafeHandle Acquire()
    {
        lock (_lock)
        {
            _refCount++;
            if (_refCount == 1)
            {
                // TODO: Call NativeMethods.prosody_init_runtime
                throw new NotImplementedException();
            }

            return _handle!;
        }
    }

    /// <summary>
    /// Releases a reference to the runtime.
    /// </summary>
    public void Release()
    {
        lock (_lock)
        {
            _refCount--;
            if (_refCount == 0)
            {
                _handle?.Dispose();
                _handle = null;
            }
        }
    }
}
