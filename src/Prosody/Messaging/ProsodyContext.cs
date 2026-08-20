using System.Text.Json;
using Prosody.State;

namespace Prosody.Messaging;

/// <summary>
/// Event context for scheduling timers, checking cancellation, and binding keyed-state collections.
/// All times are in UTC.
/// </summary>
public sealed class ProsodyContext
{
    private Native.Context? _native;
    private readonly JsonSerializerOptions? _jsonOptions;
    private readonly IReadOnlySet<StateDefinition>? _stateDefinitions;
    private readonly Dictionary<StateDefinition, object>? _stateHandles;
    private bool _expired;

    internal ProsodyContext(
        Native.Context native,
        JsonSerializerOptions jsonOptions,
        IReadOnlySet<StateDefinition> stateDefinitions
    )
    {
        ArgumentNullException.ThrowIfNull(native);
        ArgumentNullException.ThrowIfNull(jsonOptions);
        ArgumentNullException.ThrowIfNull(stateDefinitions);
        _native = native;
        _jsonOptions = jsonOptions;
        _stateDefinitions = stateDefinitions;
        _stateHandles = new Dictionary<StateDefinition, object>(ReferenceEqualityComparer.Instance);
    }

    /// <summary>Creates a stub context for unit tests that do not invoke any context methods.</summary>
    internal ProsodyContext() { }

    private object Gate => (object?)_stateHandles ?? this;

    private Native.Context ActiveNative
    {
        get
        {
            if (!Monitor.IsEntered(Gate))
            {
                throw new InvalidOperationException("Native context access requires the context gate.");
            }

            return _native
                ?? throw (
                    _expired
                        ? new TransientStateException("The handler context is no longer active.")
                        : new InvalidOperationException("This context does not have a native handler.")
                );
        }
    }

    internal void Invalidate()
    {
        lock (Gate)
        {
            _expired = true;
            _native?.Dispose();
            _native = null;
            _stateHandles?.Clear();
        }
    }

    /// <summary>
    /// Gets a value indicating whether cancellation has been requested.
    /// </summary>
    public bool ShouldCancel
    {
        get
        {
            lock (Gate)
            {
                return ActiveNative.ShouldCancel();
            }
        }
    }

    /// <summary>
    /// Returns a task that completes when cancellation is requested.
    /// </summary>
    public Task OnCancelAsync()
    {
        lock (Gate)
        {
            return ActiveNative.OnCancel();
        }
    }

    /// <summary>
    /// Schedule a new timer at the given time for the current message key.
    /// </summary>
    /// <param name="time">The time to schedule the timer (UTC).</param>
    public Task ScheduleAsync(DateTimeOffset time)
    {
        Dictionary<string, string> carrier = StateInterop.CreateCarrier();
        lock (Gate)
        {
            return ActiveNative.Schedule(time.UtcDateTime, carrier);
        }
    }

    /// <summary>
    /// Unschedule all existing timers, then schedule exactly one new timer.
    /// </summary>
    /// <param name="time">The time to schedule the timer (UTC).</param>
    public Task ClearAndScheduleAsync(DateTimeOffset time)
    {
        Dictionary<string, string> carrier = StateInterop.CreateCarrier();
        lock (Gate)
        {
            return ActiveNative.ClearAndSchedule(time.UtcDateTime, carrier);
        }
    }

    /// <summary>
    /// Unschedule a specific timer at the given time.
    /// </summary>
    /// <param name="time">The time of the timer to unschedule (UTC).</param>
    public Task UnscheduleAsync(DateTimeOffset time)
    {
        Dictionary<string, string> carrier = StateInterop.CreateCarrier();
        lock (Gate)
        {
            return ActiveNative.Unschedule(time.UtcDateTime, carrier);
        }
    }

    /// <summary>
    /// Unschedule all timers for the current key.
    /// </summary>
    public Task ClearScheduledAsync()
    {
        Dictionary<string, string> carrier = StateInterop.CreateCarrier();
        lock (Gate)
        {
            return ActiveNative.ClearScheduled(carrier);
        }
    }

    /// <summary>
    /// List all scheduled timer times for the current key.
    /// </summary>
    /// <returns>An array of scheduled times (UTC).</returns>
    public async Task<DateTimeOffset[]> ScheduledAsync()
    {
        Dictionary<string, string> carrier = StateInterop.CreateCarrier();
        Task<DateTime[]> operation;
        lock (Gate)
        {
            operation = ActiveNative.Scheduled(carrier);
        }
        DateTime[] times = await operation.ConfigureAwait(false);
        return Array.ConvertAll(times, t => new DateTimeOffset(t, TimeSpan.Zero));
    }

    /// <summary>
    /// Binds a single-value JSON collection for the current handler invocation.
    /// </summary>
    /// <typeparam name="T">The stored value type.</typeparam>
    /// <param name="definition">The collection definition. Must be registered on the client.</param>
    /// <returns>A typed handle. Repeated calls within one invocation return the same handle.</returns>
    public IValueState<T> State<T>(ValueStateDefinition<T> definition)
        where T : notnull
    {
        ArgumentNullException.ThrowIfNull(definition);
        return GetOrAddHandle(
            definition,
            options => new ValueState<T>(
                StateInterop.RunSync(() => ActiveNative.ValueState(definition.Name)),
                StateInterop.ResolveTypeInfo<T>(options)
            )
        );
    }

    /// <summary>
    /// Binds a string-keyed ordered-map JSON collection for the current handler invocation.
    /// </summary>
    /// <typeparam name="TValue">The stored value type.</typeparam>
    /// <param name="definition">The collection definition. Must be registered on the client.</param>
    /// <returns>A typed handle. Repeated calls within one invocation return the same handle.</returns>
    public IMapState<TValue> State<TValue>(MapStateDefinition<TValue> definition)
        where TValue : notnull
    {
        ArgumentNullException.ThrowIfNull(definition);
        return GetOrAddHandle(
            definition,
            options => new MapState<TValue>(
                StateInterop.RunSync(() => ActiveNative.MapState(definition.Name)),
                StateInterop.ResolveTypeInfo<TValue>(options)
            )
        );
    }

    /// <summary>
    /// Binds a deque JSON collection for the current handler invocation.
    /// </summary>
    /// <typeparam name="T">The stored element type.</typeparam>
    /// <param name="definition">The collection definition. Must be registered on the client.</param>
    /// <returns>A typed handle. Repeated calls within one invocation return the same handle.</returns>
    public IDequeState<T> State<T>(DequeStateDefinition<T> definition)
        where T : notnull
    {
        ArgumentNullException.ThrowIfNull(definition);
        return GetOrAddHandle(
            definition,
            options => new DequeState<T>(
                StateInterop.RunSync(() => ActiveNative.DequeState(definition.Name)),
                StateInterop.ResolveTypeInfo<T>(options)
            )
        );
    }

    /// <summary>
    /// Binds a single-value message collection for the current handler invocation.
    /// </summary>
    /// <typeparam name="TPayload">The message payload type.</typeparam>
    /// <param name="definition">The collection definition. Must be registered on the client.</param>
    /// <returns>A typed handle. Repeated calls within one invocation return the same handle.</returns>
    public IValueState<Message<TPayload>> State<TPayload>(MessageValueDefinition<TPayload> definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        return GetOrAddHandle(
            definition,
            options => new MessageValueState<TPayload>(
                StateInterop.RunSync(() => ActiveNative.MessageValueState(definition.Name)),
                StateInterop.ResolveTypeInfo<TPayload>(options)
            )
        );
    }

    /// <summary>
    /// Binds a string-keyed ordered-map message collection for the current handler invocation.
    /// </summary>
    /// <typeparam name="TPayload">The message payload type.</typeparam>
    /// <param name="definition">The collection definition. Must be registered on the client.</param>
    /// <returns>A typed handle. Repeated calls within one invocation return the same handle.</returns>
    public IMapState<Message<TPayload>> State<TPayload>(MessageMapDefinition<TPayload> definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        return GetOrAddHandle(
            definition,
            options => new MessageMapState<TPayload>(
                StateInterop.RunSync(() => ActiveNative.MessageMapState(definition.Name)),
                StateInterop.ResolveTypeInfo<TPayload>(options)
            )
        );
    }

    /// <summary>
    /// Binds a deque message collection for the current handler invocation.
    /// </summary>
    /// <typeparam name="TPayload">The message payload type.</typeparam>
    /// <param name="definition">The collection definition. Must be registered on the client.</param>
    /// <returns>A typed handle. Repeated calls within one invocation return the same handle.</returns>
    public IDequeState<Message<TPayload>> State<TPayload>(MessageDequeDefinition<TPayload> definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        return GetOrAddHandle(
            definition,
            options => new MessageDequeState<TPayload>(
                StateInterop.RunSync(() => ActiveNative.MessageDequeState(definition.Name)),
                StateInterop.ResolveTypeInfo<TPayload>(options)
            )
        );
    }

    private THandle GetOrAddHandle<THandle>(StateDefinition definition, Func<JsonSerializerOptions, THandle> factory)
        where THandle : class
    {
        lock (Gate)
        {
            return GetOrAddHandleLocked(definition, factory);
        }
    }

    private THandle GetOrAddHandleLocked<THandle>(
        StateDefinition definition,
        Func<JsonSerializerOptions, THandle> factory
    )
        where THandle : class
    {
        if (_expired)
        {
            throw new TransientStateException("The handler context is no longer active.");
        }

        if (_native is null || _stateHandles is null || _jsonOptions is null)
        {
            throw new InvalidOperationException("Keyed-state collections are not available on this context.");
        }

        if (_stateDefinitions?.Contains(definition) != true)
        {
            throw new PermanentStateException(
                $"State collection '{definition.Name}' must be bound with the definition object registered on the client."
            );
        }

        if (_stateHandles.TryGetValue(definition, out var cached))
        {
            return (THandle)cached;
        }

        var handle = factory(_jsonOptions);
        _stateHandles[definition] = handle;
        return handle;
    }
}
