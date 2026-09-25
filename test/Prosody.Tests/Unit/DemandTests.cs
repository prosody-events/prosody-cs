using Prosody.Messaging;
using Native = Prosody.Native;

namespace Prosody.Tests.Unit;

/// <summary>Verifies the mapping from the native demand to <see cref="Demand"/>.</summary>
public sealed class DemandTests
{
    [Theory]
    [InlineData(null, DemandKind.Normal, 0)]
    [InlineData(1U, DemandKind.Failure, 1)]
    [InlineData(7U, DemandKind.Failure, 7)]
    [InlineData(uint.MaxValue, DemandKind.Failure, int.MaxValue)]
    public void DemandMapsTheKindAndRetryOrdinal(uint? failureRetry, DemandKind kind, int retry)
    {
        Native.DemandType native = failureRetry is { } ordinal
            ? new Native.DemandType.Failure(ordinal)
            : new Native.DemandType.Normal();

        Assert.Equal(new Demand(kind, retry), Demand.FromNative(native));
    }
}
