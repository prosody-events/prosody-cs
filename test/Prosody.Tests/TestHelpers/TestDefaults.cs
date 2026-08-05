using Prosody.Infrastructure;

namespace Prosody.Tests.TestHelpers;

internal static class TestDefaults
{
    internal const string BootstrapServers = "localhost:9092";

    internal static CancelWatch NoCancelWatch => new(Watch: _ => { }, Stop: () => { });
    internal static Dictionary<string, string> EmptyCarrier => new(StringComparer.OrdinalIgnoreCase);
}
