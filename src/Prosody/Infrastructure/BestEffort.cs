namespace Prosody.Infrastructure;

/// <summary>
/// Helpers for best-effort cleanup that must never mask a primary error already propagating.
/// </summary>
internal static class BestEffort
{
    /// <summary>
    /// Awaits <paramref name="operation"/> for its side effect, swallowing any fault so it can never
    /// replace or mask the primary error being propagated by the caller.
    /// </summary>
    /// <param name="operation">The cleanup operation to run.</param>
    /// <returns>A task that completes when the operation finishes or faults.</returns>
    internal static async ValueTask RunAsync(Func<Task> operation)
    {
        try
        {
            await operation().ConfigureAwait(false);
        }
#pragma warning disable CA1031, RCS1075 // Best-effort cleanup must swallow all faults so the primary error is never masked.
        catch (Exception)
        {
            // Intentionally swallowed: the primary error is already propagating.
        }
#pragma warning restore CA1031, RCS1075
    }
}
