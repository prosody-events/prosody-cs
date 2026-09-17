using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Prosody.Configuration;
using Prosody.Extensions;
using Prosody.Tests.TestHelpers;

namespace Prosody.Tests.Unit;

/// <summary>Tests repeated registration, validation, and the shared client instance.</summary>
public sealed class ProsodyClientRegistrationTests : AsyncDisposalTestBase
{
    private static IConfiguration MockConfiguration(string section = "Prosody") =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    [$"{section}:BootstrapServers:0"] = TestDefaults.BootstrapServers,
                    [$"{section}:GroupId"] = "test-group",
                    [$"{section}:Mock"] = "true",
                }
            )
            .Build();

    [Fact]
    public async Task AddProsodyClientTwiceRegistersOneClientAndAppliesBothConfigureActions()
    {
        var services = new ServiceCollection();
        services.AddSingleton(MockConfiguration());

        services.AddProsodyClient(options => options.MaxConcurrency = 7);
        services.AddProsodyClient(options => options.SourceSystem = "second-call");

        Assert.Single(services, d => d.ServiceType == typeof(ProsodyClient));
        Assert.Single(services, d => d.ImplementationType == typeof(ProsodyClientLifecycle));
        await using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<ClientOptions>>().Value;
        Assert.Equal(7u, options.MaxConcurrency);
        Assert.Equal([TestDefaults.BootstrapServers], options.BootstrapServers!);
        Assert.Equal("second-call", provider.GetRequiredService<ProsodyClient>().SourceSystem);
    }

    [Fact]
    public void AddProsodyClientWithADifferentSectionThrows()
    {
        var services = new ServiceCollection();
        services.AddSingleton(MockConfiguration("First"));
        services.AddProsodyClient("First", options => options.SourceSystem = "first");

        var error = Assert.Throws<InvalidOperationException>(() =>
            services.AddProsodyClient("Second", options => options.SourceSystem = "second")
        );

        Assert.Contains("'First'", error.Message, StringComparison.Ordinal);
        using var provider = services.BuildServiceProvider();
        Assert.Equal("first", provider.GetRequiredService<IOptions<ClientOptions>>().Value.SourceSystem);
    }

    [Fact]
    public async Task ProviderAndClientResolveToOneInstance()
    {
        var services = new ServiceCollection();
        services.AddSingleton(MockConfiguration());
        services.AddProsodyClient();
        var provider = Track(services.BuildServiceProvider());

#pragma warning disable CS0618 // The adapter is kept for one minor; this test pins that it still resolves.
        var legacy = provider.GetRequiredService<ProsodyClientProvider>();
        Func<Task<ProsodyClient>> methodGroup = legacy.GetAsync;
        var fromProvider = await methodGroup();
#pragma warning restore CS0618
        Assert.Same(provider.GetRequiredService<ProsodyClient>(), fromProvider);
    }

    [Fact]
    public void MissingSourceSystemFailsOptionsValidation()
    {
        var services = new ServiceCollection();
        services.AddSingleton(MockConfiguration());
        services.AddProsodyClient(options => options.GroupId = null);
        using var provider = services.BuildServiceProvider();

        Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<ProsodyClient>());
    }
}
