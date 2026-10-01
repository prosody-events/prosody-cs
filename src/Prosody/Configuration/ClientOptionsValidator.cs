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
}
