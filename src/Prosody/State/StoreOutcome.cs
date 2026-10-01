namespace Prosody.State;

/// <summary>
/// The effect of <c>CommitAsync</c> or <c>RollbackAsync</c> on a keyed-state collection.
/// </summary>
public enum StoreOutcome
{
    /// <summary>The call drained buffered operations: a commit wrote them, or a rollback discarded them.</summary>
    Applied = 0,

    /// <summary>Nothing was buffered, so the call had no effect.</summary>
    NoOp = 1,
}
