namespace Prosody.Tests.TestHelpers;

/// <summary>
/// Disposes every tracked resource after each test, including failed tests.
/// </summary>
public abstract class AsyncDisposalTestBase : IAsyncLifetime
{
    private readonly List<IAsyncDisposable> _resources = [];

    protected T Track<T>(T resource)
        where T : IAsyncDisposable
    {
        _resources.Add(resource);
        return resource;
    }

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        try
        {
            await Task.WhenAll(_resources.Select(static resource => resource.DisposeAsync().AsTask()));
        }
        finally
        {
            GC.SuppressFinalize(this);
        }
    }
}
