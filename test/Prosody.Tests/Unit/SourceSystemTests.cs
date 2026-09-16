using System.Diagnostics;
using System.Globalization;
using Prosody.Configuration;
using Prosody.Tests.TestHelpers;

namespace Prosody.Tests.Unit;

/// <summary>Tests source system precedence and parity with the native client.</summary>
[Collection("Sequential")]
public sealed class SourceSystemTests
{
    private const string _scenarioVariable = "PROSODY_TEST_SOURCE_SCENARIO";

    private static readonly (
        string? Source,
        string? EnvironmentSource,
        string? Group,
        string? EnvironmentGroup,
        string Expected
    )[] Scenarios =
    [
        (null, "env-source", null, null, "env-source"),
        (null, null, null, "env-group", "env-group"),
        (null, null, "group-only", null, "group-only"),
        (null, null, "group", "env-group", "group"),
        (null, "env-source", "group", "env-group", "env-source"),
        ("explicit", "env-source", "group", "env-group", "explicit"),
    ];

    [Fact]
    public async Task SourceSystemIsKnownBeforeConnectAndMatchesTheNativeClient()
    {
        if (Environment.GetEnvironmentVariable(_scenarioVariable) is { } scenario)
        {
            await AssertSourceSystemAsync(int.Parse(scenario, CultureInfo.InvariantCulture));
            return;
        }

        // Set variables before process startup so managed and native code read the same environment.
        for (var i = 0; i < Scenarios.Length; i++)
        {
            var start = new ProcessStartInfo("dotnet")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            start.ArgumentList.Add(typeof(SourceSystemTests).Assembly.Location);
            start.ArgumentList.Add("--filter-method");
            start.ArgumentList.Add(
                $"{typeof(SourceSystemTests).FullName}.{nameof(SourceSystemIsKnownBeforeConnectAndMatchesTheNativeClient)}"
            );
            start.Environment[_scenarioVariable] = i.ToString(CultureInfo.InvariantCulture);
            start.Environment["PROSODY_SOURCE_SYSTEM"] = Scenarios[i].EnvironmentSource;
            start.Environment["PROSODY_GROUP_ID"] = Scenarios[i].EnvironmentGroup;
            // .NET 8 passes null values as empty strings. Remove absent variables explicitly.
            if (Scenarios[i].EnvironmentSource is null)
            {
                start.Environment.Remove("PROSODY_SOURCE_SYSTEM");
            }
            if (Scenarios[i].EnvironmentGroup is null)
            {
                start.Environment.Remove("PROSODY_GROUP_ID");
            }

            using var process = Process.Start(start)!;
            var output = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
            var error = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
            try
            {
                await process
                    .WaitForExitAsync(TestContext.Current.CancellationToken)
                    .WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
                Assert.True(process.ExitCode == 0, $"Scenario {i}:\n{await output}\n{await error}");
            }
            finally
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync(CancellationToken.None);
                }
            }
        }
    }

    private static async Task AssertSourceSystemAsync(int scenario)
    {
        var (source, _, group, _, expected) = Scenarios[scenario];
        var options = new ClientOptions
        {
            Mock = true,
            BootstrapServers = [TestDefaults.BootstrapServers],
            SourceSystem = source,
            GroupId = group,
        };
        Task<Native.ProsodyClient>? build = null;
        await using var client = new ProsodyClient(
            options,
            () => build = Native.ProsodyClient.ProsodyClientAsync(options.ToNative())
        );

        Assert.Equal(expected, client.SourceSystem);
        Assert.Null(build);

        await client.ConnectAsync(TestContext.Current.CancellationToken);

        Assert.NotNull(build);
        Assert.Equal(expected, (await build).SourceSystem());
    }
}
