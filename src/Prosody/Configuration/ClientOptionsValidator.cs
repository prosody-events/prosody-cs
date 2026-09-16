using Microsoft.Extensions.Options;
using Prosody.State;

namespace Prosody.Configuration;

internal sealed class ClientOptionsValidator : IValidateOptions<ClientOptions>
{
    /// <summary>
    /// The client waits on native shutdown for this timeout plus a fixed margin. The bound keeps
    /// that sum far inside what <see cref="Task.WaitAsync(TimeSpan)"/> accepts.
    /// </summary>
    internal static readonly TimeSpan MaxShutdownTimeout = TimeSpan.FromDays(1);

    public ValidateOptionsResult Validate(string? name, ClientOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        List<string> failures = [];

        CheckTimeSpans(options, failures);
        CheckShutdownTimeout(options, failures);
        CheckStateCollections(options, failures);
        CheckSourceSystem(options, failures);

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    /// <summary>Checks the resolved value, so the environment variable path is covered too.</summary>
    private static void CheckShutdownTimeout(ClientOptions options, List<string> failures)
    {
        const string name = nameof(ClientOptions.ShutdownTimeout);
        TimeSpan? timeout;
        try
        {
            timeout = options.ResolveShutdownTimeout();
        }
        catch (OverflowException)
        {
            failures.Add($"{name}, or PROSODY_SHUTDOWN_TIMEOUT, must not exceed {MaxShutdownTimeout}.");
            return;
        }

        if (timeout < TimeSpan.Zero)
        {
            failures.Add($"{name} must not be negative.");
        }
        else if (timeout > MaxShutdownTimeout)
        {
            failures.Add($"{name}, or PROSODY_SHUTDOWN_TIMEOUT, must not exceed {MaxShutdownTimeout}.");
        }
    }

    private static void CheckSourceSystem(ClientOptions options, List<string> failures)
    {
        if (options.ResolveSourceSystem() is null)
        {
            failures.Add(
                "SourceSystem or GroupId must be set, or PROSODY_SOURCE_SYSTEM or PROSODY_GROUP_ID must be present in the environment."
            );
        }
    }

    private static void CheckStateCollections(ClientOptions options, List<string> failures)
    {
        if (options.StateCollections is not { } definitions)
        {
            return;
        }

        for (var i = 0; i < definitions.Length; i++)
        {
            if (definitions[i] is null)
            {
                failures.Add($"StateCollections[{i}] must not be null.");
            }
        }
    }

    private static void CheckTimeSpans(ClientOptions options, List<string> failures)
    {
        CheckNonNegative(options.Timeout, nameof(ClientOptions.Timeout), failures);
        CheckNonNegative(options.StallThreshold, nameof(ClientOptions.StallThreshold), failures);
        CheckNonNegative(options.PollInterval, nameof(ClientOptions.PollInterval), failures);
        CheckNonNegative(options.CommitInterval, nameof(ClientOptions.CommitInterval), failures);
        CheckNonNegative(options.SlabSize, nameof(ClientOptions.SlabSize), failures);
        CheckNonNegative(options.SendTimeout, nameof(ClientOptions.SendTimeout), failures);
        CheckNonNegative(options.RetryBase, nameof(ClientOptions.RetryBase), failures);
        CheckNonNegative(options.MaxRetryDelay, nameof(ClientOptions.MaxRetryDelay), failures);
        CheckNonNegative(options.DeferBase, nameof(ClientOptions.DeferBase), failures);
        CheckNonNegative(options.DeferMaxDelay, nameof(ClientOptions.DeferMaxDelay), failures);
        CheckNonNegative(options.DeferFailureWindow, nameof(ClientOptions.DeferFailureWindow), failures);
        CheckNonNegative(options.LoaderSeekTimeout, nameof(ClientOptions.LoaderSeekTimeout), failures);
        CheckNonNegative(options.MonopolizationWindow, nameof(ClientOptions.MonopolizationWindow), failures);
        CheckNonNegative(options.SchedulerMaxWait, nameof(ClientOptions.SchedulerMaxWait), failures);
        CheckNonNegative(options.CassandraRetention, nameof(ClientOptions.CassandraRetention), failures);
    }

    private static void CheckNonNegative(TimeSpan? value, string name, List<string> failures)
    {
        if (value is { Ticks: < 0 })
        {
            failures.Add($"{name} must not be negative.");
        }
    }
}
