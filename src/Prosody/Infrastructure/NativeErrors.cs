using Prosody.Errors;
using Prosody.State;

namespace Prosody.Infrastructure;

/// <summary>
/// Translates a native failure into a public exception. No public call lets a
/// <see cref="Native.FfiException"/> escape.
/// </summary>
/// <remarks>
/// The native variant decides the public type, never the message. A caller mistake becomes an
/// <see cref="ArgumentException"/> or an <see cref="InvalidOperationException"/>. A keyed-state failure
/// keeps its <see cref="StateException"/> category. Every other failure becomes a
/// <see cref="ProsodyException"/>. Only <see cref="PermanentStateException"/> implements
/// <see cref="IPermanentError"/>, so the handler bridge classifies every other translated error transient.
/// </remarks>
internal static class NativeErrors
{
    /// <summary>Maps <paramref name="error"/> to its public exception type.</summary>
    /// <param name="error">The native failure.</param>
    /// <param name="paramName">The parameter that a caller mistake names, when the call site knows it.</param>
    internal static Exception Translate(Native.FfiException error, string? paramName = null) =>
        error switch
        {
            Native.FfiException.Cancelled => new OperationCanceledException(error.Message, error),
            Native.FfiException.PermanentState => new PermanentStateException(error.Message, error),
            Native.FfiException.TransientState => new TransientStateException(error.Message, error),
            Native.FfiException.CompactDateTime => new ArgumentOutOfRangeException(paramName, error.Message),
            Native.FfiException.InvalidArgument
            or Native.FfiException.TopicContainsNul
            or Native.FfiException.AdminConfiguration
            or Native.FfiException.TopicConfiguration => new ArgumentException(error.Message, paramName, error),
            Native.FfiException.InvalidOperation
            or Native.FfiException.ConsumerConfiguration
            or Native.FfiException.CassandraConfiguration
            or Native.FfiException.LoaderConfig
            or Native.FfiException.TelemetryConfig => new InvalidOperationException(error.Message, error),
            _ => new ProsodyException(error.Message, error),
        };

    /// <summary>Runs one synchronous native call and translates its failure.</summary>
    internal static void Run(Action operation)
    {
        try
        {
            operation();
        }
        catch (Native.FfiException error)
        {
            throw Translate(error);
        }
    }

    /// <summary>Runs one synchronous native call that returns a value and translates its failure.</summary>
    internal static T Run<T>(Func<T> operation)
    {
        try
        {
            return operation();
        }
        catch (Native.FfiException error)
        {
            throw Translate(error);
        }
    }

    /// <summary>Runs one asynchronous native call and translates its failure.</summary>
    /// <param name="operation">The native call.</param>
    /// <param name="paramName">The parameter that a caller mistake names, when the call site knows it.</param>
    internal static async Task RunAsync(Func<Task> operation, string? paramName = null)
    {
        try
        {
            await operation().ConfigureAwait(false);
        }
        catch (Native.FfiException error)
        {
            throw Translate(error, paramName);
        }
    }

    /// <summary>Runs one asynchronous native call that returns a value and translates its failure.</summary>
    internal static async Task<T> RunAsync<T>(Func<Task<T>> operation)
    {
        try
        {
            return await operation().ConfigureAwait(false);
        }
        catch (Native.FfiException error)
        {
            throw Translate(error);
        }
    }
}
