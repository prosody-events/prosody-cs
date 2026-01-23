using System.Runtime.InteropServices;

namespace Prosody.Native;

/// <summary>
/// Safe handle for the native Prosody runtime context.
/// </summary>
internal sealed class ProsodyRuntimeSafeHandle : SafeHandle
{
    /// <inheritdoc />
    public override bool IsInvalid => handle == IntPtr.Zero;

    /// <summary>
    /// Initializes a new instance of the <see cref="ProsodyRuntimeSafeHandle"/> class.
    /// </summary>
    /// <param name="handle">The native handle.</param>
    public ProsodyRuntimeSafeHandle(IntPtr handle) : base(IntPtr.Zero, true)
    {
        SetHandle(handle);
    }

    /// <inheritdoc />
    protected override bool ReleaseHandle()
    {
        // TODO: Call NativeMethods.prosody_dispose_runtime
        return true;
    }
}

/// <summary>
/// Safe handle for the native Prosody client context.
/// </summary>
internal sealed class ProsodyClientSafeHandle : SafeHandle
{
    private ProsodyRuntimeSafeHandle? _parent;

    /// <inheritdoc />
    public override bool IsInvalid => handle == IntPtr.Zero;

    /// <summary>
    /// Initializes a new instance of the <see cref="ProsodyClientSafeHandle"/> class.
    /// </summary>
    /// <param name="handle">The native handle.</param>
    public ProsodyClientSafeHandle(IntPtr handle) : base(IntPtr.Zero, true)
    {
        SetHandle(handle);
    }

    /// <summary>
    /// Sets the parent runtime handle to prevent it from being disposed.
    /// </summary>
    /// <param name="parent">The parent runtime handle.</param>
    public void SetParent(ProsodyRuntimeSafeHandle parent)
    {
        var addRef = false;
        parent.DangerousAddRef(ref addRef);
        if (addRef)
        {
            _parent = parent;
        }
    }

    /// <inheritdoc />
    protected override bool ReleaseHandle()
    {
        // TODO: Call NativeMethods.prosody_dispose_client
        _parent?.DangerousRelease();
        return true;
    }
}

/// <summary>
/// Safe handle for the native event context.
/// </summary>
internal sealed class ProsodyContextSafeHandle : SafeHandle
{
    /// <inheritdoc />
    public override bool IsInvalid => handle == IntPtr.Zero;

    /// <summary>
    /// Initializes a new instance of the <see cref="ProsodyContextSafeHandle"/> class.
    /// </summary>
    /// <param name="handle">The native handle.</param>
    public ProsodyContextSafeHandle(IntPtr handle) : base(IntPtr.Zero, true)
    {
        SetHandle(handle);
    }

    /// <inheritdoc />
    protected override bool ReleaseHandle()
    {
        // Context handles are typically not owned by C# - they're borrowed during callbacks
        return true;
    }
}
