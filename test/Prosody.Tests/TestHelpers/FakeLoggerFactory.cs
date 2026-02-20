using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;

namespace Prosody.Tests.TestHelpers;

internal sealed class FakeLoggerFactory(FakeLogCollector collector) : ILoggerFactory
{
    public FakeLogCollector Collector { get; } = collector;

    public FakeLoggerFactory()
        : this(new FakeLogCollector()) { }

    public ILogger CreateLogger(string categoryName) => new FakeLogger(Collector, categoryName);

    public void AddProvider(ILoggerProvider provider) { }

    public void Dispose() { }
}
