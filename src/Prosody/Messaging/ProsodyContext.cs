using System.Text.Json;
using Prosody.State;

namespace Prosody.Messaging;

/// <summary>
/// Event context for scheduling timers, checking cancellation, and binding keyed-state collections.
/// All times are in UTC.
/// </summary>
public sealed class ProsodyContext
{
    private readonly Native.Context _native;
    private readonly JsonSerializerOptions? _jsonOptions;
    private readonly Dictionary<string, object>? _stateHandles;

    internal ProsodyContext(Native.Context native, JsonSerializerOptions jsonOptions)
    {
        ArgumentNullException.ThrowIfNull(native);
        ArgumentNullException.ThrowIfNull(jsonOptions);
        _native = native;
        _jsonOptions = jsonOptions;
        _stateHandles = new Dictionary<string, object>(StringComparer.Ordinal);
    }

    /// <summary>Creates a stub context for unit tests that do not invoke any context methods.</summary>
    internal ProsodyContext() => _native = null!;

    /// <summary>
    /// Gets a value indicating whether cancellation has been requested.
    /// </summary>
    public bool ShouldCancel => _native.ShouldCancel();

    /// <summary>
    /// Returns a task that completes when cancellation is requested.
    /// </summary>
    public Task OnCancelAsync() => _native.OnCancel();

    /// <summary>
    /// Schedule a new timer at the given time for the current message key.
    /// </summary>
    /// <param name="time">The time to schedule the timer (UTC).</param>
    public Task ScheduleAsync(DateTimeOffset time)
    {
        Dictionary<string, string> carrier = CreateCarrier();
        return _native.Schedule(time.UtcDateTime, carrier);
    }

    /// <summary>
    /// Unschedule all existing timers, then schedule exactly one new timer.
    /// </summary>
    /// <param name="time">The time to schedule the timer (UTC).</param>
    public Task ClearAndScheduleAsync(DateTimeOffset time)
    {
        Dictionary<string, string> carrier = CreateCarrier();
        return _native.ClearAndSchedule(time.UtcDateTime, carrier);
    }

    /// <summary>
    /// Unschedule a specific timer at the given time.
    /// </summary>
    /// <param name="time">The time of the timer to unschedule (UTC).</param>
    public Task UnscheduleAsync(DateTimeOffset time)
    {
        Dictionary<string, string> carrier = CreateCarrier();
        return _native.Unschedule(time.UtcDateTime, carrier);
    }

    /// <summary>
    /// Unschedule all timers for the current key.
    /// </summary>
    public Task ClearScheduledAsync()
    {
        Dictionary<string, string> carrier = CreateCarrier();
        return _native.ClearScheduled(carrier);
    }

    /// <summary>
    /// List all scheduled timer times for the current key.
    /// </summary>
    /// <returns>An array of scheduled times (UTC).</returns>
    public async Task<DateTimeOffset[]> ScheduledAsync()
    {
        Dictionary<string, string> carrier = CreateCarrier();
        DateTime[] times = await _native.Scheduled(carrier).ConfigureAwait(false);
        return Array.ConvertAll(times, t => new DateTimeOffset(t, TimeSpan.Zero));
    }

    /// <summary>
    /// Binds a single-value JSON collection for the current handler invocation.
    /// </summary>
    /// <typeparam name="T">The stored value type.</typeparam>
    /// <param name="definition">The collection definition. Must be registered on the client.</param>
    /// <returns>A typed handle. Repeated calls within one invocation return the same handle.</returns>
    public IValueState<T> State<T>(ValueStateDefinition<T> definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        return GetOrAddHandle(
            definition,
            options => new ValueState<T>(
                StateInterop.RunSync(() => _native.ValueState(definition.Name)),
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
    {
        ArgumentNullException.ThrowIfNull(definition);
        return GetOrAddHandle(
            definition,
            options => new MapState<TValue>(
                StateInterop.RunSync(() => _native.MapState(definition.Name)),
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
    {
        ArgumentNullException.ThrowIfNull(definition);
        return GetOrAddHandle(
            definition,
            options => new DequeState<T>(
                StateInterop.RunSync(() => _native.DequeState(definition.Name)),
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
                StateInterop.RunSync(() => _native.MessageValueState(definition.Name)),
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
                StateInterop.RunSync(() => _native.MessageMapState(definition.Name)),
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
                StateInterop.RunSync(() => _native.MessageDequeState(definition.Name)),
                StateInterop.ResolveTypeInfo<TPayload>(options)
            )
        );
    }

    private THandle GetOrAddHandle<THandle>(StateDefinition definition, Func<JsonSerializerOptions, THandle> factory)
        where THandle : class
    {
        if (_native is null || _stateHandles is null || _jsonOptions is null)
        {
            throw new InvalidOperationException("Keyed-state collections are not available on this context.");
        }

        var cacheKey = $"{definition.Kind}:{definition.Payload}:{definition.Name}";
        if (_stateHandles.TryGetValue(cacheKey, out var cached))
        {
            return (THandle)cached;
        }

        var handle = factory(_jsonOptions);
        _stateHandles[cacheKey] = handle;
        return handle;
    }

    private static Dictionary<string, string> CreateCarrier() => StateInterop.CreateCarrier();
}
