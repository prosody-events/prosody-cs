using System.Runtime.InteropServices;

namespace Prosody.Messaging;

/// <summary>
/// The demand that one handler call serves. Read it from <see cref="ProsodyContext.Demand"/>.
/// </summary>
/// <param name="Kind">Whether the call is a first attempt or a retry after a failure.</param>
/// <param name="Retry">
/// The retry ordinal. It is 0 for <see cref="DemandKind.Normal"/> and 1 on the first retry. It is an
/// estimate: it starts again at 1 when Prosody defers an event after immediate retries. Keep an exact
/// attempt count in keyed state if the handler needs one.
/// </param>
[StructLayout(LayoutKind.Auto)]
public readonly record struct Demand(DemandKind Kind, int Retry)
{
    /// <summary>Converts the native demand. A retry ordinal above <see cref="int.MaxValue"/> saturates.</summary>
    internal static Demand FromNative(Native.DemandType demand) =>
        demand switch
        {
            Native.DemandType.Normal => new Demand(DemandKind.Normal, 0),
            Native.DemandType.Failure failure => new Demand(
                DemandKind.Failure,
                (int)Math.Min(failure.Retry, int.MaxValue)
            ),
            _ => throw new ArgumentOutOfRangeException(nameof(demand), demand, "Unknown native demand."),
        };
}
