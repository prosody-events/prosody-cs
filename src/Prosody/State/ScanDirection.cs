namespace Prosody.State;

/// <summary>
/// The direction a scan walks a collection's key or index order.
/// </summary>
public enum ScanDirection
{
    /// <summary>Ascending key or index order.</summary>
    Forward = 0,

    /// <summary>Descending key or index order.</summary>
    Backward = 1,
}
