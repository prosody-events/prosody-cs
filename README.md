# Prosody: C# Bindings for Kafka

Prosody offers C# bindings to the [Prosody Kafka client](https://github.com/prosody-events/prosody), providing
features for message production and consumption, including configurable retry mechanisms, failure handling
strategies, and integrated OpenTelemetry support for distributed tracing.

## Features

- **Kafka Consumer**: Per-key ordering with cross-key concurrency, offset management, consumer groups
- **Kafka Producer**: Idempotent delivery with configurable retries
- **Timer System**: Persistent scheduled execution backed by Cassandra or in-memory store
- **Keyed State**: Per-key persistent state (value/map/deque) with transactional, at-least-once semantics
- **Quality of Service**: Fair scheduling limits concurrency and prevents failures from starving fresh traffic. Pipeline mode adds deferred retry and monopolization detection
- **Distributed Tracing**: OpenTelemetry integration for tracing message flow across services
- **Error Monitoring**: Optional Sentry integration for automatic handler exception reporting
- **Backpressure**: Pauses partitions when handlers fall behind
- **Mocking**: In-memory Kafka broker for tests (`WithMock(true)`)
- **Failure Handling**: Pipeline (retry forever), Low-Latency (dead letter), Best-Effort (log and skip)

## Installation

Add the NuGet package to your project:

```bash
dotnet add package ProsodyEvents.Prosody
```

## Quick Start

```csharp
using Prosody;

// Initialize the client with the builder pattern
await using var client = await ProsodyClientBuilder.Create()
    // Bootstrap servers should normally be set using the PROSODY_BOOTSTRAP_SERVERS environment variable
    .WithBootstrapServers("localhost:9092")
    // To allow loopbacks, the SourceSystem must be different from the GroupId.
    // Normally, the SourceSystem would be left unspecified, which would default to the GroupId.
    .WithSourceSystem("my-application-source")
    // The GroupId should be set to the name of your application
    .WithGroupId("my-application")
    // Topics the client should subscribe to
    .WithSubscribedTopics("my-topic")
    .BuildAsync();

// Define a message handler
var messageHandler = new MyHandler();

// Subscribe to messages using the message handler
await client.SubscribeAsync(messageHandler);

// Send a message to a topic
await client.SendAsync("my-topic", "message-key", new { Content = "Hello, Kafka!" });
await client.ExciseAsync("my-topic", "obsolete-key");

// Ensure proper shutdown when done
await client.ShutdownAsync();

// Handler implementation
public class MyHandler : IProsodyHandler<MyPayload>
{
    public Task OnExciseAsync(ProsodyContext prosodyContext, ExciseMessage message, CancellationToken cancellationToken)
    {
        Console.WriteLine($"Excise key: {message.Key}");
        return Task.CompletedTask;
    }

    public async Task OnMessageAsync(ProsodyContext prosodyContext, Message<MyPayload> message, CancellationToken cancellationToken)
    {
        // Process the received message
        var payload = message.Payload;
        Console.WriteLine($"Received message: {payload}");

        // Schedule a timer for delayed processing (requires Cassandra unless Mock = true)
        if (payload?.ScheduleFollowup == true)
        {
            var futureTime = DateTimeOffset.UtcNow.AddSeconds(30);
            await prosodyContext.ScheduleAsync(futureTime);
        }
    }

    public Task OnTimerAsync(ProsodyContext prosodyContext, ProsodyTimer timer, CancellationToken cancellationToken)
    {
        // Handle timer firing
        Console.WriteLine($"Timer fired for key: {timer.Key} at {timer.Time}");
        return Task.CompletedTask;
    }
}
```

## Excise records

Call `ExciseAsync(topic, key)` to send a Kafka record with a key and no payload. Use this record to delete the key from compacted views.

Each handler must implement `OnExciseAsync`. It receives an `ExciseMessage` with record metadata and no payload.

## Architecture

Prosody enables efficient, parallel processing of Kafka messages while maintaining order for messages with the same key:

- **Partition-Level Parallelism**: Separate management of each Kafka partition
- **Key-Based Queuing**: Ordered processing for each key within a partition
- **Concurrent Processing**: Simultaneous processing of different keys
- **Backpressure Management**: Pause consumption from backed-up partitions

## Quality of Service

All modes use **fair scheduling** to limit concurrency and distribute execution time. Pipeline mode adds **deferred
retry** and **monopolization detection**.

### Fair Scheduling (All Modes)

The scheduler controls which message runs next and how many run concurrently.

**Virtual Time (VT):** Each key accumulates VT equal to its handler execution time. The scheduler picks the key with the
lowest VT. A key that runs for 500ms accumulates 500ms of VT; a key that hasn't run recently has zero VT and gets
priority.

**Two-Class Split:** Normal messages and failure retries have separate VT pools. The scheduler allocates execution time
between them (default: 70% normal, 30% failure). During a failure spike, retries get at most 30% of execution time—fresh
messages continue processing.

**Starvation Prevention:** Tasks receive a quadratic priority boost based on wait time. A task waiting 2 minutes
(configurable) gets maximum boost, overriding VT disadvantage.

### Deferred Retry (Pipeline Mode)

Moves failing keys to timer-based retry so the partition can continue processing other keys.

On transient failure: store the message offset in Cassandra, schedule a timer, return success. The partition advances.
When the timer fires, reload the message from Kafka and retry.

```csharp
// Configure defer behavior
await using var client = await ProsodyClientBuilder.Create()
    .WithGroupId("my-consumer-group")
    .WithSubscribedTopics("my-topic")
    .Configure(options =>
    {
        options.DeferEnabled = true;                              // Enable deferral (default: true)
        options.DeferBase = TimeSpan.FromSeconds(1);              // Wait 1s before first retry
        options.DeferMaxDelay = TimeSpan.FromHours(24);           // Cap at 24 hours
        options.DeferFailureThreshold = 0.9;                      // Disable when >90% failing
    })
    .BuildAsync();
```

**Failure Rate Gating:** When >90% of recent messages fail, deferral disables. The retry middleware blocks the
partition, applying backpressure upstream.

### Monopolization Detection (Pipeline Mode)

Rejects keys that consume too much execution time.

The middleware tracks per-key execution time in 5-minute rolling windows. Keys exceeding 90% of window time are rejected
with a transient error, routing them through defer.

```csharp
// Configure monopolization detection
await using var client = await ProsodyClientBuilder.Create()
    .WithGroupId("my-consumer-group")
    .WithSubscribedTopics("my-topic")
    .Configure(options =>
    {
        options.MonopolizationEnabled = true;                     // Enable detection (default: true)
        options.MonopolizationThreshold = 0.9;                    // Reject keys using >90% of window
        options.MonopolizationWindow = TimeSpan.FromMinutes(5);   // 5-minute window
    })
    .BuildAsync();
```

### Handler Timeout

Handlers are automatically cancelled if they exceed a deadline:

```csharp
await using var client = await ProsodyClientBuilder.Create()
    .WithGroupId("my-consumer-group")
    .WithSubscribedTopics("my-topic")
    .Configure(options =>
    {
        options.Timeout = TimeSpan.FromSeconds(30);               // Cancel after 30 seconds
        options.StallThreshold = TimeSpan.FromSeconds(60);        // Report unhealthy after 60 seconds
    })
    .BuildAsync();
```

When a handler times out, `prosodyContext.ShouldCancel` becomes `true` and the `CancellationToken` is cancelled. The handler
should exit promptly. If not specified, timeout defaults to 80% of `StallThreshold`.

## Configuration

For the complete configuration reference, see [CONFIGURATION.md](CONFIGURATION.md).

`ClientOptions` properties take precedence. Unset properties use environment variables, then library defaults.

Client construction is asynchronous. Use `ProsodyClient.CreateAsync` or `ProsodyClientBuilder.BuildAsync`.

## Liveness and Readiness Probes

Prosody includes a built-in probe server for consumer-based applications that provides health check endpoints. The probe
server is tied to the consumer's lifecycle and offers two main endpoints:

1. `/readyz`: A readiness probe that checks if any partitions are assigned to the consumer. Returns a success status
   only when the consumer has at least one partition assigned, indicating it's ready to process messages.

2. `/livez`: A liveness probe that checks if any partitions have stalled (haven't processed a message within a
   configured time threshold).

Configure the probe server using the builder:

```csharp
await using var client = await ProsodyClientBuilder.Create()
    .WithGroupId("my-consumer-group")
    .WithSubscribedTopics("my-topic")
    .WithProbePort(8000)                                          // Set to 0 to disable
    .Configure(options =>
    {
        options.StallThreshold = TimeSpan.FromSeconds(15);        // 15 seconds before considering a partition stalled
    })
    .BuildAsync();
```

Or via environment variables:

```bash
PROSODY_PROBE_PORT=8000  # Set to 0 to disable
PROSODY_STALL_THRESHOLD=15s  # Default stall detection threshold
```

### Important Notes

1. The probe server starts automatically when the consumer is subscribed and stops when unsubscribed.
2. A partition is considered "stalled" if it hasn't processed a message within the `StallThreshold` duration.
3. The stall threshold should be set based on your application's message processing latency and expected message
   frequency.
4. Setting the threshold too low might cause false positives, while setting it too high could delay detection of actual
   issues.
5. The probe server is only active when consuming messages (not for producer-only usage).

You can monitor the stall state programmatically using the client's methods:

```csharp
// Get the number of partitions currently assigned to this consumer
var partitionCount = await client.AssignedPartitionCountAsync();

// Check if the consumer has stalled partitions
if (await client.IsStalledAsync())
{
    Console.WriteLine("Consumer has stalled partitions");
}
```

## Requests

Requests return one outcome for each named subsystem. The result dictionary uses canonical subsystem names as keys.

Use `RequestExciseAsync` to send an excise record and collect the same outcome type.

Do not rely on dictionary enumeration order.

Prosody throws an exception if the request cannot produce the complete result dictionary.

Do not await a request from a handler for the same key and subsystem. The request cannot finish before that handler returns.

Return a JSON response from each message handler:

```csharp
public sealed record Order(string Type);
public sealed record InventoryResponse(string Accepted);

public sealed class InventoryHandler : IProsodyRequestHandler<Order, InventoryResponse>
{
    public Task<InventoryResponse> OnMessageAsync(
        ProsodyContext context,
        Message<Order> message,
        CancellationToken cancellationToken
    ) => Task.FromResult(new InventoryResponse(message.Key));

    public Task<InventoryResponse> OnExciseAsync(
        ProsodyContext context,
        ExciseMessage message,
        CancellationToken cancellationToken
    ) => Task.FromResult(new InventoryResponse(message.Key));

    public Task OnTimerAsync(
        ProsodyContext context,
        ProsodyTimer timer,
        CancellationToken cancellationToken
    ) => Task.CompletedTask;
}
```

Message handler return values become successful request outcomes. Each return value must have a JSON representation.

Only message results become request responses. Timer results are not request responses.

Send a request without a subscription on the requester:

```csharp
string[] subsystems = ["inventory", "billing"];
IReadOnlyDictionary<string, Outcome<InventoryResponse>> results = await client.RequestAsync<Order, InventoryResponse>(
    "orders",
    "order-1",
    new Order("order.created"),
    subsystems,
    TimeSpan.FromSeconds(2)
);

foreach (var (subsystem, outcome) in results)
{
    if (outcome is Success<InventoryResponse> success)
    {
        Console.WriteLine($"{subsystem}: {success.Value}");
    }
    else if (outcome is Failure<InventoryResponse> failure)
    {
        Console.Error.WriteLine($"{subsystem}: {failure.Error.Message}");
    }
}
```

The example can print these results:

```text
inventory: InventoryResponse { Accepted = order-1 }
billing: no response arrived before the deadline
```

Each `Failure<T>` contains one typed response error.

Each response error uses Prosody's message.

Local JSON errors use the .NET decoder's message.

JSON `null` remains a successful result. Use a nullable response type when a handler can return `null`.

Use the `JsonTypeInfo` overload for trim-safe request and response serialization.

## Advanced Usage

### Pipeline Mode

Pipeline mode is the default mode. Ensures ordered processing, retrying failed operations indefinitely:

```csharp
// Initialize client in pipeline mode
await using var client = await ProsodyClientBuilder.Create()
    .WithMode(ClientMode.Pipeline)  // Explicitly set pipeline mode (this is the default)
    .WithGroupId("my-consumer-group")
    .WithSubscribedTopics("my-topic")
    .BuildAsync();
```

### Low-Latency Mode

Prioritizes quick processing, sending persistently failing messages to a failure topic:

```csharp
// Initialize client in low-latency mode
await using var client = await ProsodyClientBuilder.Create()
    .WithMode(ClientMode.LowLatency)  // Set low-latency mode
    .WithGroupId("my-consumer-group")
    .WithSubscribedTopics("my-topic")
    .WithFailureTopic("failed-messages")  // Specify a topic for failed messages
    .BuildAsync();
```

### Best-Effort Mode

Optimized for development environments or services where message processing failures are acceptable:

```csharp
// Initialize client in best-effort mode
await using var client = await ProsodyClientBuilder.Create()
    .WithMode(ClientMode.BestEffort)  // Set best-effort mode
    .WithGroupId("my-consumer-group")
    .WithSubscribedTopics("my-topic")
    .BuildAsync();
```

## Event Type Filtering

Prosody supports filtering messages based on event type prefixes, allowing your consumer to process only specific types
of events:

```csharp
// Process only events with types starting with "user." or "account."
await using var client = await ProsodyClientBuilder.Create()
    .WithGroupId("my-consumer-group")
    .WithSubscribedTopics("my-topic")
    .WithAllowedEvents("user.", "account.")
    .BuildAsync();
```

Or via environment variables:

```bash
PROSODY_ALLOWED_EVENTS=user.,account.
```

### Matching Behavior

Prefixes must match exactly from the start of the event type:

✓ Matches:

- `{"type": "user.created"}` matches prefix `user.`
- `{"type": "account.deleted"}` matches prefix `account.`

✗ No Match:

- `{"type": "admin.user.created"}` doesn't match `user.`
- `{"type": "my.account.deleted"}` doesn't match `account.`
- `{"type": "notification"}` doesn't match any prefix

If no prefixes are configured, all messages are processed. Messages without a `type` field are always processed.

## Source System Deduplication

Prosody prevents processing loops in distributed systems by tracking the source of each message:

```csharp
// Consumer and producer in one application
await using var client = await ProsodyClientBuilder.Create()
    .WithGroupId("my-service")
    .WithSourceSystem("my-service-producer")  // Must differ from GroupId to allow loopbacks; defaults to GroupId
    .WithSubscribedTopics("my-topic")
    .BuildAsync();
```

Or via environment variable:

```bash
PROSODY_SOURCE_SYSTEM=my-service-producer
```

### How It Works

1. **Producers** add a `source-system` header to all outgoing messages.
2. **Consumers** check this header on incoming messages.
3. If a message's source system matches the consumer's group ID, the message is skipped.

This prevents endless loops where a service consumes its own produced messages.

## Message Deduplication

Prosody automatically deduplicates messages using the `id` field in their JSON payload. Consecutive messages with the
same ID and key are processed only once.

```csharp
// Messages with IDs are deduplicated per key
await client.SendAsync("my-topic", "key1", new
{
    Id = "msg-123",      // Message will be processed
    Content = "Hello!"
});

await client.SendAsync("my-topic", "key1", new
{
    Id = "msg-123",      // Message will be skipped (duplicate)
    Content = "Hello again!"
});

await client.SendAsync("my-topic", "key2", new
{
    Id = "msg-123",      // Message will be processed (different key)
    Content = "Hello!"
});
```

Deduplication uses a global in-memory cache shared across all partitions, which survives partition reassignments within
the same process. For cross-restart deduplication, a Cassandra-backed persistent store is used when Cassandra is
configured.

Deduplication is always active. `IdempotenceCacheSize` must be greater than `0`; a value of `0` (via either the
option or `PROSODY_IDEMPOTENCE_CACHE_SIZE=0`) is rejected when the client is built. The cache capacity can be tuned:

```csharp
await using var client = await ProsodyClientBuilder.Create()
    .WithGroupId("my-consumer-group")
    .WithSubscribedTopics("my-topic")
    .Configure(options => options.IdempotenceCacheSize = 16384)   // Tune the shared cache capacity
    .BuildAsync();
```

To invalidate all previously recorded dedup entries and force reprocessing, change the version string:

```csharp
.Configure(options => options.IdempotenceVersion = "2")          // Invalidate all prior dedup records
```

The Cassandra TTL for dedup records defaults to 7 days and can be adjusted:

```csharp
.Configure(options => options.IdempotenceTtl = TimeSpan.FromDays(14))  // Keep records for 14 days
```

Note that in-memory deduplication is best-effort and not guaranteed. Duplicates can still occur when instances restart
if Cassandra is not configured.

## Timer Functionality

Prosody supports timer-based delayed execution within message handlers. When a timer fires, your handler's `OnTimerAsync` method will be called:

```csharp
public class MyHandler : IProsodyHandler<MyPayload>
{
    public Task OnExciseAsync(
        ProsodyContext prosodyContext,
        ExciseMessage message,
        CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public async Task OnMessageAsync(ProsodyContext prosodyContext, Message<MyPayload> message, CancellationToken cancellationToken)
    {
        // Schedule a timer to fire in 30 seconds
        var futureTime = DateTimeOffset.UtcNow.AddSeconds(30);
        await prosodyContext.ScheduleAsync(futureTime);

        // Schedule multiple timers
        var oneMinute = DateTimeOffset.UtcNow.AddMinutes(1);
        var twoMinutes = DateTimeOffset.UtcNow.AddMinutes(2);
        await prosodyContext.ScheduleAsync(oneMinute);
        await prosodyContext.ScheduleAsync(twoMinutes);

        // Check what's scheduled
        var scheduledTimes = await prosodyContext.ScheduledAsync();
        Console.WriteLine($"Scheduled timers: {scheduledTimes.Length}");
    }

    public Task OnTimerAsync(ProsodyContext prosodyContext, ProsodyTimer timer, CancellationToken cancellationToken)
    {
        Console.WriteLine("Timer fired!");
        Console.WriteLine($"Key: {timer.Key}");
        Console.WriteLine($"Scheduled time: {timer.Time}");
        return Task.CompletedTask;
    }
}
```

### Timer Methods

The context provides timer scheduling methods that allow you to delay execution or implement timeout behavior:

- `ScheduleAsync(DateTimeOffset time)`: Schedules a timer to fire at the specified time
- `ClearAndScheduleAsync(DateTimeOffset time)`: Clears all timers and schedules a new one
- `UnscheduleAsync(DateTimeOffset time)`: Removes a timer scheduled for the specified time
- `ClearScheduledAsync()`: Removes all scheduled timers
- `ScheduledAsync()`: Returns an array of all scheduled timer times

### Timer Object

When a timer fires, the `OnTimerAsync` method receives a timer object with these properties:

- `Key` (string): The entity key identifying what this timer belongs to
- `Time` (DateTimeOffset): The time when this timer was scheduled to fire

**Note**: Timer precision is limited to seconds due to the underlying storage format. Sub-second precision in scheduled times will be rounded to the nearest second.

### Timer Configuration

Timer functionality requires Cassandra for persistence unless running in mock mode. Configure Cassandra connection via environment variable:

```bash
PROSODY_CASSANDRA_NODES=localhost:9042  # Required for timer persistence
```

Or programmatically when creating the client:

```csharp
await using var client = await ProsodyClientBuilder.Create()
    .WithBootstrapServers("localhost:9092")
    .WithGroupId("my-application")
    .WithSubscribedTopics("my-topic")
    .Configure(options => options.CassandraNodes = ["localhost:9042"])  // Required unless Mock = true
    .BuildAsync();
```

For testing, you can use mock mode to avoid Cassandra dependency:

```csharp
// Mock mode for testing (timers work but aren't persisted)
await using var client = await ProsodyClientBuilder.Create()
    .WithBootstrapServers("localhost:9092")
    .WithGroupId("my-application")
    .WithSubscribedTopics("my-topic")
    .WithMock(true)  // No Cassandra required in mock mode
    .BuildAsync();
```

## Keyed State

Stream handlers usually receive one event at a time. Many decisions need facts from earlier events. Counters, activity windows, and workflows all need this memory.

A Kafka key identifies the entity for an event, such as a customer or order. Keyed state gives each key separate, durable memory. Prosody selects the current message or timer key automatically. Prosody also runs only one handler for that key at a time.

State survives process restarts and Kafka partition moves. By default, Prosody makes changes visible only after the event succeeds. A failed attempt cannot publish its pending changes.

Use keyed state for counters, deduplication, rolling totals, pending work, and per-key workflows. Use a database for business records, joins, and ad hoc queries. Repeated database reads can make stream processing slow and expensive.

Give most collections a TTL. Set the TTL beyond the longest timer or workflow that uses the collection. Omit it only when inactive keys must remain forever.

### A counter for each key

Declare each collection once, register it on the client, and ask the event context for the current key's state:

```csharp
var count = StateDefinition.Value<int>("count", ttl: TimeSpan.FromDays(30));

public sealed class CountHandler(ValueStateDefinition<int> count)
    : IProsodyHandler<Event>
{
    public Task OnExciseAsync(
        ProsodyContext context,
        ExciseMessage message,
        CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public async Task OnMessageAsync(
        ProsodyContext context,
        Message<Event> message,
        CancellationToken cancellationToken)
    {
        var state = context.State(count);
        var current = (await state.GetAsync(cancellationToken)).GetValueOrDefault(0);
        await state.SetAsync(current + 1, cancellationToken);
    }

    public Task OnTimerAsync(
        ProsodyContext context,
        ProsodyTimer timer,
        CancellationToken cancellationToken) =>
        Task.CompletedTask;
}

var client = await ProsodyClientBuilder.Create()
    .WithStateCollections(count)
    .BuildAsync();
```

Each Kafka key now has an independent counter. A counter expires when that key has no update for 30 days.

### Window activity into one notification

This example groups a burst of activity for one user. It sends the first event immediately. It collects later events for five minutes.

The timer sends one summary when the window ends. The user ID is the Kafka key, so each user has an independent window.

```csharp
var window = StateDefinition.Value<bool>("window", ttl: TimeSpan.FromDays(1));
var pending = StateDefinition.MessageDeque<Activity>(
    "pending", ttl: TimeSpan.FromDays(1), capacity: 100);

public async Task OnMessageAsync(
    ProsodyContext context,
    Message<Activity> message,
    CancellationToken cancellationToken)
{
    var windowState = context.State(window);
    var pendingState = context.State(pending);

    if ((await windowState.GetAsync(cancellationToken)).GetValueOrDefault(false))
    {
        await pendingState.PushBackAsync(message, cancellationToken);
        return;
    }

    await Notify(message.Key, [message]);
    await windowState.SetAsync(true, cancellationToken);
    await context.ClearAndScheduleAsync(
        DateTimeOffset.UtcNow + TimeSpan.FromMinutes(5));
}

public async Task OnTimerAsync(
    ProsodyContext context,
    ProsodyTimer timer,
    CancellationToken cancellationToken)
{
    var pendingState = context.State(pending);
    var batch = new List<Message<Activity>>();
    await foreach (var message in pendingState.WithCancellation(cancellationToken))
        batch.Add(message);

    if (batch.Count > 0) await Notify(timer.Key, batch);
    await pendingState.ClearAsync(cancellationToken);
    await context.State(window).ClearAsync(cancellationToken);
}
```

See the complete, compiled example for imports, types, client setup, and `NotifyAsync`: [`examples/keyed_state_windowing.cs`](examples/keyed_state_windowing.cs).

Why this works:

- Register both definitions with `WithStateCollections` before `BuildAsync()`. Keyed state uses Cassandra unless `Mock = true`.
- Use `ClearAndScheduleAsync`, not `ScheduleAsync`, so a retried event does not add another timer for the same key.
- `capacity: 100` and the one-day TTL bound the saved backlog. Overflow drops the oldest message because this example pushes at the back.
- A `MessageDeque` requires the original Kafka messages during the window. Use `Deque` when topic retention or compaction cannot provide them.
- Prosody runs one handler at a time for each key, so a user's message and timer handlers cannot overlap.
- A notification is outside the state transaction. A retry can send it again. Use a stable idempotency key when duplicates matter.

### Collections and handles

A definition sets a collection's durable name, kind, and options. Register each definition once on the client.

Pass the same definition to `context.State()` inside a handler. Prosody uses the current event key for that handle.

Do not reuse a durable name for a different collection kind or payload type. Create handles inside the handler. Do not retain handles or iterators.

| Collection | JSON payload | Kafka message | Main operations |
| --- | --- | --- | --- |
| Value | `StateDefinition.Value<T>` | `StateDefinition.MessageValue<TPayload>` | `GetAsync`, `SetAsync`, `ClearAsync` |
| Ordered string map | `StateDefinition.Map<TValue>` | `StateDefinition.MessageMap<TPayload>` | `GetAsync`, `GetManyAsync`, `ContainsKeyAsync`, `SetAsync`, `RemoveAsync`, `EnumerateAsync`, `ClearAsync` |
| Deque | `StateDefinition.Deque<T>` | `StateDefinition.MessageDeque<TPayload>` | `PushBackAsync`, `PushFrontAsync`, `PopBackAsync`, `PopFrontAsync`, `GetAsync`, `CountAsync`, `EnumerateAsync`, `ClearAsync` |

Map and deque scans use `await foreach`. Map keys are strings.

Reads return `StateValue<T>`. This type distinguishes an absent value from a stored `default(T)`. Use `ClearAsync` or `RemoveAsync` instead of storing `null`.

Payload types guide JSON serialization. They do not add runtime validation.

### When changes become visible

Reads inside a handler see earlier writes from that handler. By default, Prosody buffers changes until the event succeeds.

Prosody then publishes the changes together. If the handler throws, none of its pending changes become visible.

Each collection also offers explicit controls for workflows that need different behavior:

- `readUncommitted: true` writes changes before Prosody records the event as complete. A crash can make these changes visible before a retry. Use this option only when repeated processing produces the same result.
- `await state.CommitAsync()` immediately publishes the collection's pending changes. A later handler failure does not remove them.
- `await state.RollbackAsync()` discards pending changes since the last `CommitAsync()`. It cannot undo committed changes.

Keyed-state payloads use the client's `JsonSerializerOptions`. For AOT or trimmed builds, include every state payload type in the source-generated `JsonSerializerContext`; see [AOT / Trim-safe Usage](#aot--trim-safe-usage).

### Published state

Handlers normally read state only for their current event key. Sometimes another service needs that state without consuming the owner's Kafka topics.

Published state provides this read-only access. The owner enables publication, names its subsystem, and registers the collection definition:

```csharp
var currentOrder = StateDefinition.Value<Order>("current-order", published: true);
var options = new ClientOptions
{
    GroupId = "order-writer",
    Subsystem = "checkout",
    StateCollections = [currentOrder],
};

// The handler uses the key from its current event.
var ownedOrder = context.State(currentOrder);
await ownedOrder.SetAsync(updatedOrder, cancellationToken);
```

Another client uses the subsystem and the same definition to open a reader. The reader does not require a subscription:

```csharp
PublishedValue<Order> orderReader = await client.StateAsync("checkout", currentOrder);
StateValue<Order> value = await orderReader.GetAsync("customer-123", cancellationToken);
```

The reader returns only committed state. It cannot change the collection. Each read takes an explicit key because no handler supplies one.

Map and deque readers return `IAsyncEnumerable<T>`. They fetch data in chunks. Pass `ScanDirection.Backward` to read in reverse order.

The default cache window is five seconds. Set `readCache: StateReadCache.For(ttl)` to select a different window. Use `StateReadCache.Disabled` to bypass the cache.

To stop publication, deploy the definition with `published: false`. Keep the definition registered and keep its `Subsystem` during that deployment.

## OpenTelemetry Tracing

Prosody supports OpenTelemetry tracing, allowing you to monitor and analyze the performance of your Kafka-based
applications. The library will emit traces using the OTLP protocol if the `OTEL_EXPORTER_OTLP_ENDPOINT` environment
variable is defined.

Note: Prosody emits its own traces separately because it uses its own tracing runtime, as it would be expensive to send
all traces to C#.

### Required Packages

To use OpenTelemetry tracing with Prosody, you need to install the following packages:

```bash
dotnet add package OpenTelemetry
dotnet add package OpenTelemetry.Extensions.Hosting
dotnet add package OpenTelemetry.Exporter.OpenTelemetryProtocol
```

### Initializing Tracing

To initialize tracing in your application:

```csharp
using OpenTelemetry;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService("my-service-name"))
    .WithTracing(tracing => tracing
        .AddSource("my-service-name")
        .AddOtlpExporter());

var app = builder.Build();
```

### Setting OpenTelemetry Environment Variables

Set the following standard OpenTelemetry environment variables:

```bash
OTEL_EXPORTER_OTLP_ENDPOINT=http://localhost:4318
OTEL_EXPORTER_OTLP_PROTOCOL=http/protobuf
OTEL_SERVICE_NAME=my-service-name
```

For more information on these and other OpenTelemetry environment variables, refer to
the [OpenTelemetry specification](https://opentelemetry.io/docs/specs/otel/configuration/sdk-environment-variables/#general-sdk-configuration).

### Using Tracing in Your Application

After initializing tracing, you can define spans in your application, and they will be properly propagated through
Kafka:

```csharp
using System.Diagnostics;

public class MyHandler : IProsodyHandler<MyPayload>
{
    private static readonly ActivitySource ActivitySource = new("my-service-name");

    public Task OnExciseAsync(
        ProsodyContext prosodyContext,
        ExciseMessage message,
        CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public async Task OnMessageAsync(ProsodyContext prosodyContext, Message<MyPayload> message, CancellationToken cancellationToken)
    {
        using var activity = ActivitySource.StartActivity("process-message");

        // Process the received message
        activity?.AddEvent(new ActivityEvent("message.received"));

        Console.WriteLine($"Received message: {message.Payload}");
    }

    public Task OnTimerAsync(ProsodyContext prosodyContext, ProsodyTimer timer, CancellationToken cancellationToken) => Task.CompletedTask;
}
```

### Span Linking

By default, message execution spans use **`Child`** (child-of relationship — the execution span is part of
the same trace as the producer). Timer execution spans use **`FollowsFrom`** (the execution span starts a
new trace with a span link back to the scheduling span, since timer execution is causally related but not part of
the same operation).

Both strategies are configurable via the `MessageSpans` / `PROSODY_MESSAGE_SPANS` and `TimerSpans` /
`PROSODY_TIMER_SPANS` options. Accepted values: `child`, `follows_from`.

## Best Practices

### Ensuring Thread-Safe Handlers

Your event handler methods will be called concurrently from multiple threads. NEVER use mutable shared state across
event handler calls, like setting instance variables. Sharing state can introduce subtle data races and corruption
that may only appear in production. If you must use shared state, use appropriate synchronization primitives like
`lock`, `SemaphoreSlim`, or concurrent collections.

### Ensuring Idempotent Message Handlers

Idempotent message handlers are crucial for maintaining data consistency, fault tolerance, and scalability when working
with distributed, event-based systems. They ensure that processing a message multiple times has the same effect as
processing it once, which is essential for recovering from failures.

Strategies for achieving idempotence:

1. **Natural Idempotence**: Use inherently idempotent operations (e.g., setting a value in a key-value store).

2. **Deduplication with Unique Identifiers**:

- Kafka messages can be uniquely identified by their partition and offset.
- Before processing, check if the message has been handled before.
- Store processed message identifiers with an appropriate TTL.

3. **Database Upserts**: Use upsert operations for database writes (e.g., `MERGE` in SQL Server or
   `INSERT ... ON CONFLICT DO UPDATE` in PostgreSQL via EF Core).

4. **Partition Offset Tracking**:

- Store the latest processed offset for each partition.
- Only process messages with higher offsets than the last processed one.
- Critically, store these offsets transactionally with other state updates to ensure consistency.

5. **Idempotency Keys for External APIs**: Utilize idempotency keys when supported by external APIs.

6. **Check-then-Act Pattern**:

- For non-idempotent external systems, verify if an operation was previously completed before execution.
- Maintain a record of completed operations, keyed by a unique message identifier.

7. **Saga Pattern**:

- Implement a state machine in your database for multi-step operations.
- Each message advances the state machine, allowing for idempotent processing and easy failure recovery.
- Particularly useful for complex, distributed transactions across multiple services.

### Proper Shutdown

Shut down the client before your application exits:

```csharp
// Ensure proper shutdown
await client.ShutdownAsync();
```

This ensures:

1. Completion and commitment of all in-flight work
2. Quick rebalancing, allowing other consumers to take over partitions
3. Proper release of resources

Implement shutdown handling in your application using `IHostedService` or `IHostApplicationLifetime`:

```csharp
using Microsoft.Extensions.Hosting;
using Prosody;

public class ProsodyWorker : BackgroundService
{
    private readonly ProsodyClientProvider _clients;

    public ProsodyWorker(ProsodyClientProvider clients) => _clients = clients;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var client = await _clients.GetAsync();
        await client.SubscribeAsync(new MyHandler());

        // Wait for shutdown signal
        await Task.Delay(Timeout.Infinite, stoppingToken);
    }

}
```

### Error Handling

Prosody classifies errors as transient (temporary, can be retried) or permanent (won't be resolved by retrying). By
default, all errors are considered transient.

The attribute, marker interface, and classifier apply to message, excise, and timer methods.

#### Using Attributes

Use the `[PermanentError]` attribute to classify exceptions that should not be retried:

```csharp
using Prosody;
using System.Text.Json;

public class MyHandler : IProsodyHandler<MyPayload>
{
    public Task OnExciseAsync(ProsodyContext prosodyContext, ExciseMessage message, CancellationToken cancellationToken) => Task.CompletedTask;

    [PermanentError(typeof(JsonException), typeof(ArgumentException))]
    public async Task OnMessageAsync(ProsodyContext prosodyContext, Message<MyPayload> message, CancellationToken cancellationToken)
    {
        // Your message handling logic here
        // JsonException and ArgumentException will be treated as permanent
        // All other exceptions will be treated as transient (default behavior)
    }

    public Task OnTimerAsync(ProsodyContext prosodyContext, ProsodyTimer timer, CancellationToken cancellationToken) => Task.CompletedTask;
}
```

#### Using PermanentException

You can also throw a `PermanentException` directly:

```csharp
using Prosody;

public class MyHandler : IProsodyHandler<MyPayload>
{
    public Task OnExciseAsync(ProsodyContext prosodyContext, ExciseMessage message, CancellationToken cancellationToken) => Task.CompletedTask;

    public async Task OnMessageAsync(ProsodyContext prosodyContext, Message<MyPayload> message, CancellationToken cancellationToken)
    {
        var payload = message.Payload;

        if (payload?.Version < MinimumSupportedVersion)
        {
            throw new PermanentException("Message version is no longer supported");
        }

        // Process message...
    }

    public Task OnTimerAsync(ProsodyContext prosodyContext, ProsodyTimer timer, CancellationToken cancellationToken) => Task.CompletedTask;
}
```

#### Using IPermanentError Interface

For custom exception types, implement the `IPermanentError` marker interface:

```csharp
using Prosody;

public class ValidationException : Exception, IPermanentError
{
    public ValidationException(string message) : base(message) { }
}
```

#### Best Practices for Error Handling

- Use permanent errors for issues like malformed data or business logic violations.
- Use transient errors for temporary issues like network problems.
- Be cautious with permanent errors as they prevent retries and can result in data loss.
- Consider system reliability and data consistency when classifying errors.

### Handling Task Cancellation

Prosody cancels tasks during partition rebalancing or timeout. During shutdown, handlers run freely for most of the shutdown timeout before the cancellation signal fires — giving in-flight work time to complete. How you handle cancellation is critical:

- A handler that returns normally (no exception) is considered **successful** — Prosody treats the message as processed.
- Any exception — including `OperationCanceledException` — signals **failure**. Prosody does not distinguish
  cancellation from other errors; all exceptions are classified as transient (or permanent if marked).
- **Never silently return on cancellation.** If the handler returns without an exception, Prosody assumes the message was
  fully processed. Swallowing cancellation (e.g., `if (cancellationToken.IsCancellationRequested) return;`) tells Prosody
  the message succeeded when it didn't, which can cause data loss.

The correct pattern is to let `OperationCanceledException` propagate. When Prosody initiates the cancellation (rebalance,
timeout, shutdown), it already knows the handler didn't complete — the transient error result simply confirms this. Prosody
will not naively retry a message it just cancelled during shutdown; the retry behavior depends on the operating mode and
the reason for cancellation.

The library provides a `CancellationToken` to your handler methods. Pass this token to any async operations that support
it to ensure prompt cancellation.

Best practices:

1. **Throw, don't swallow.** Use `ThrowIfCancellationRequested()` or pass the token to async APIs that throw on
   cancellation. Never check `IsCancellationRequested` and silently return — this breaks the cancellation contract
   and causes Prosody to treat incomplete work as successful.
2. Exit promptly when cancelled to avoid rebalancing delays.
3. Use `try/finally` blocks for clean resource handling.
4. Pass the `CancellationToken` to all async operations that support it.

Example of using CancellationToken in message processing:

```csharp
public class MyHandler : IProsodyHandler<MyPayload>
{
    private readonly HttpClient _httpClient;
    private readonly MyDbContext _dbContext;
    private readonly ProsodyClient _client;

    public Task OnExciseAsync(ProsodyContext prosodyContext, ExciseMessage message, CancellationToken cancellationToken) => Task.CompletedTask;

    public async Task OnMessageAsync(ProsodyContext prosodyContext, Message<MyPayload> message, CancellationToken cancellationToken)
    {
        // Pass the token to HTTP calls — throws OperationCanceledException on cancellation
        var response = await _httpClient.GetAsync("https://api.example.com", cancellationToken);
        var data = await response.Content.ReadAsStringAsync(cancellationToken);

        // Pass the token to database operations
        await _dbContext.Messages.AddAsync(new MessageEntity { Payload = data }, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);

        // Send a message, passing the cancellation token
        await _client.SendAsync("topic", "key", new { Data = "value" }, cancellationToken);
    }

    public Task OnTimerAsync(ProsodyContext prosodyContext, ProsodyTimer timer, CancellationToken cancellationToken) => Task.CompletedTask;
}
```

For CPU-bound loops, poll `ThrowIfCancellationRequested()` periodically. This throws `OperationCanceledException` when
cancellation is requested, correctly signaling to Prosody that the handler did not complete:

```csharp
public class MyHandler : IProsodyHandler<List<Item>>
{
    public Task OnExciseAsync(ProsodyContext prosodyContext, ExciseMessage message, CancellationToken cancellationToken) => Task.CompletedTask;

    public async Task OnMessageAsync(ProsodyContext prosodyContext, Message<List<Item>> message, CancellationToken cancellationToken)
    {
        foreach (var item in message.Payload ?? [])
        {
            // Correct: throws OperationCanceledException, signaling incomplete work
            cancellationToken.ThrowIfCancellationRequested();

            ProcessItem(item);
        }
    }

    public Task OnTimerAsync(ProsodyContext prosodyContext, ProsodyTimer timer, CancellationToken cancellationToken) => Task.CompletedTask;
}
```

**Anti-pattern** — do not silently return on cancellation:

```csharp
// WRONG: Prosody sees success and commits the offset, losing the unprocessed message
foreach (var item in items)
{
    if (cancellationToken.IsCancellationRequested)
        return; // Silent return = Prosody thinks the message was fully processed

    ProcessItem(item);
}
```

Failing to follow these practices can lead to:

- **Data loss** from incomplete work being marked as successful when cancellation is silently swallowed.
- Slower message processing due to delayed rebalancing.
- Resource leaks if long-running operations aren't properly cancelled.

## Logging Configuration

Prosody provides flexible logging integration with your application.

### Static Configuration

```csharp
using Microsoft.Extensions.Logging;
using Prosody;
using Prosody.Logging;

// Configure logging globally for all Prosody clients (must be called once, before creating clients)
var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
ProsodyLogging.Configure(loggerFactory);
```

To reset logging in test fixtures (e.g., during teardown so `Configure` can be called again):

```csharp
ProsodyLogging.ResetForTesting();
```

### Dependency Injection

For ASP.NET Core or Generic Host applications:

```csharp
var builder = Host.CreateApplicationBuilder(args);

// Auto-configures Prosody logging with the host's ILoggerFactory
builder.Services.AddProsodyLogging();
builder.Services.AddProsodyClient();

var host = builder.Build();
```

Inject `ProsodyClientProvider` into hosted services. Call `GetAsync` to get the shared client.
The provider disposes the client when the host stops.
A failed `GetAsync` call does not poison the provider. A later call retries client construction.
Use asynchronous host disposal when possible. Synchronous disposal starts client shutdown without blocking.

Log messages are emitted under the `Prosody.Native` category.

## Error Monitoring (Sentry)

Prosody automatically reports handler exceptions to [Sentry](https://sentry.io) when the host application has Sentry initialized. Prosody never calls `SentrySdk.Init` — it only enriches an already-initialized Sentry instance.

### Setup

Initialize Sentry in your host application before subscribing to messages:

```csharp
SentrySdk.Init(o =>
{
    o.Dsn = "https://examplePublicKey@o0.ingest.sentry.io/0";
    o.Environment = "production";
    o.Release = "my-app@1.2.3";
});
```

Or with ASP.NET Core / Generic Host:

```csharp
builder.WebHost.UseSentry("https://examplePublicKey@o0.ingest.sentry.io/0");
```

If Sentry is not initialized, Prosody silently skips error reporting with zero overhead.

### How It Works

Prosody checks `SentrySdk.IsEnabled` on each handler failure. If the host has Sentry initialized, Prosody captures the exception and enriches it with handler context. Prosody never owns the Sentry lifecycle — initialization and disposal remain entirely in the host application.

### What Gets Reported

Both transient and permanent handler exceptions are captured with contextual data:

- `prosody.event_type` tag: `"message"` or `"timer"`
- `prosody.error_class` tag: `"permanent"` or `"transient"`
- `prosody` context:
  - For messages: topic, key, partition, offset
  - For timers: key, fire time

### Safety Guarantee

Sentry failures never affect message processing. If Sentry is unreachable or misconfigured, the exception is logged and handler results are unchanged.

> **Note:** The `Sentry` package is currently a hard dependency of `ProsodyEvents.Prosody`. A future improvement is to extract Sentry support into a separate `ProsodyEvents.Prosody.Sentry` package so consumers who don't use Sentry don't pull in the dependency.

## Administrative Operations

**⚠️ Important Note**: Topic management in production environments should typically be handled through GitOps using
Strimzi KafkaTopic manifests. The `AdminClient` is provided for testing scenarios and specific cases where manual
topic creation and deletion is required.

### AdminClient

The `AdminClient` provides administrative operations for Kafka topics:

```csharp
using Prosody;

// Initialize admin client
using var admin = new AdminClient("localhost:9092");

// Create a topic for testing
await admin.CreateTopicAsync(
    name: "test-topic",
    partitionCount: 4,
    replicationFactor: 1
);

// Delete a topic
await admin.DeleteTopicAsync("test-topic");
```

#### Configuration Parameters

The `AdminClient` constructor accepts:

- `bootstrapServers` (params string[]): Kafka bootstrap servers (required)

Or via environment variable:

```bash
PROSODY_BOOTSTRAP_SERVERS=localhost:9092  # Single server
PROSODY_BOOTSTRAP_SERVERS=localhost:9092,localhost:9093  # Multiple servers
```

## Release Process

Prosody uses an automated release process managed by GitHub Actions. Here's an overview of how releases are handled:

1. **Trigger**: The release process is triggered automatically on pushes to the `main` branch.

2. **Release Please**: The process starts with the "Release Please" action, which:
    - Analyzes commit messages since the last release.
    - Creates or updates a release pull request with changelog updates and version bumps.
    - When the PR is merged, it creates a GitHub release and a git tag.

3. **Build Process**: If a new release is created, the following native build jobs are triggered:
    - Linux builds for x86_64 and aarch64 architectures.
    - Windows builds for x64 and arm64 architectures.
    - macOS builds for arm64 (Apple Silicon) architecture.

4. **Pack**: A single NuGet package (`ProsodyEvents.Prosody`) is assembled from the native artifacts and the generated
   C# bindings, bundling all supported runtimes under `runtimes/<rid>/native/` inside the `.nupkg`.

5. **Test**: The packed `.nupkg` is consumed by the test project (via `TestPackage=true`) and run against Kafka and
   Cassandra on each supported RID / target framework combination (.NET 8, 9, 10) before publication.

6. **Publication**: If all tests pass, the package is published to [nuget.org](https://www.nuget.org/packages/ProsodyEvents.Prosody).

### Contributing to Releases

To contribute to a release:

1. Make your changes in a feature branch.
2. Use [Conventional Commits](https://www.conventionalcommits.org/) syntax for your commit messages. This helps Release
   Please determine the next version number and generate the changelog.
3. Create a pull request to merge your changes into the `main` branch.
4. Once your PR is approved and merged, Release Please will include your changes in the next release PR.

### Manual Releases

While the process is automated, manual intervention may sometimes be necessary:

- You can manually trigger the release workflow from the GitHub Actions tab if needed (including the `release_as`
  input to force a specific version, e.g. `2.2.0-beta.1`).
- If you need to make changes to the release PR created by Release Please, you can do so before merging it.

All releases are automatically published to nuget.org. Ensure you have thoroughly tested your changes before merging
to `main`.

## API Reference

### ProsodyClientBuilder

Fluent builder for configuring and creating a ProsodyClient. All `With*` methods return the builder for chaining.

- `static ProsodyClientBuilder Create()`: Creates a new builder instance.

**Builder Methods:**
- `WithBootstrapServers(params string[] servers)`: Set Kafka bootstrap servers
- `WithGroupId(string groupId)`: Set consumer group ID
- `WithSubscribedTopics(params string[] topics)`: Set topics to subscribe to
- `WithMode(ClientMode mode)`: Set client operating mode
- `WithAllowedEvents(params string[] prefixes)`: Set event type prefixes to allow
- `WithSourceSystem(string sourceSystem)`: Set source system identifier
- `WithMock(bool mock)`: Enable/disable in-memory mock client
- `WithMaxConcurrency(uint maxConcurrency)`: Set max concurrent messages
- `WithMaxRetries(uint maxRetries)`: Set max retry attempts
- `WithFailureTopic(string topic)`: Set dead letter topic
- `WithProbePort(ushort port)`: Set health check probe port
- `WithSendTimeout(TimeSpan timeout)`: Set max time to wait for message delivery
- `Configure(Action<ClientOptions> configure)`: Set any option on `ClientOptions` directly
- `ConfigureJsonOptions(Action<JsonSerializerOptions> configure)`: Override JSON serialization options (runs after defaults are applied)
- `WithStateCollections(params StateDefinition[] definitions)`: Register keyed-state collections before subscribe

**Build:**
- `Task<ProsodyClient> BuildAsync()`: Validates configuration and creates a client asynchronously.

### ProsodyClient

- `Task<ProsodyClient> ProsodyClient.CreateAsync(ClientOptions options)`: Create a client asynchronously.
- `string SourceSystem { get; }`: Get the source system identifier configured for the client.
- `Task<ConsumerState> GetConsumerStateAsync()`: Get the current state of the consumer.
- `Task<uint> AssignedPartitionCountAsync()`: Get the number of partitions currently assigned to this consumer.
- `Task<bool> IsStalledAsync()`: Check if the consumer has stalled partitions.
- `Task<PublishedValue<T>> StateAsync<T>(string subsystem, ValueStateDefinition<T> definition, CancellationToken cancellationToken = default)`: Open a read-only published value.
- `Task<PublishedMap<TValue>> StateAsync<TValue>(string subsystem, MapStateDefinition<TValue> definition, CancellationToken cancellationToken = default)`: Open a read-only published map.
- `Task<PublishedDeque<T>> StateAsync<T>(string subsystem, DequeStateDefinition<T> definition, CancellationToken cancellationToken = default)`: Open a read-only published deque.
- `Task SendAsync<T>(string topic, string key, T payload, CancellationToken cancellationToken = default)`: Send a message to a specified topic (uses configured `JsonSerializerOptions`; annotated with `[RequiresUnreferencedCode]`).
- `Task ExciseAsync(string topic, string key, CancellationToken cancellationToken = default)`: Send an excise record for a key.
- `Task SendAsync<T>(string topic, string key, T payload, JsonTypeInfo<T> typeInfo, CancellationToken cancellationToken = default)`: Trim-clean overload; serializes using the supplied `JsonTypeInfo<T>` instead of the client's options.
- `Task<IReadOnlyDictionary<string, Outcome<TResponse>>> RequestAsync<TPayload, TResponse>(...)`: Return one outcome for each subsystem.
- `Task<IReadOnlyDictionary<string, Outcome<TResponse>>> RequestAsync<TPayload, TResponse>(..., JsonTypeInfo<TPayload>, JsonTypeInfo<TResponse>, ...)`: Send a trim-safe request.
- `Task<IReadOnlyDictionary<string, Outcome<TResponse>>> RequestExciseAsync<TResponse>(...)`: Send an excise request.
- `Task SubscribeAsync<T>(IProsodyHandler<T> handler)`: Subscribe to messages using a strongly typed payload handler (annotated with `[RequiresUnreferencedCode]`).
- `Task SubscribeAsync<T>(IProsodyHandler<T> handler, IPermanentErrorClassifier classifier)`: Trim-clean overload; bypasses `[PermanentError]` attribute reflection.
- `Task SubscribeAsync<TPayload, TResponse>(IProsodyRequestHandler<TPayload, TResponse> handler)`: Subscribe with typed request responses.
- `Task SubscribeAsync<TPayload, TResponse>(IProsodyRequestHandler<TPayload, TResponse> handler, IPermanentErrorClassifier classifier)`: Use explicit request-handler error classification.
- `Task UnsubscribeAsync()`: Stop the consumer. You can subscribe again later.
- `Task ShutdownAsync()`: Stop all client services. Concurrent and repeated calls await the same operation.
- `void Dispose()`: Dispose of client resources synchronously.
- `ValueTask DisposeAsync()`: Shut down and dispose of client resources. Enables `await using`.

### AdminClient

- `AdminClient(params string[] bootstrapServers)`: Initialize a new AdminClient with the given configuration.
- `Task CreateTopicAsync(string name, ushort partitionCount, ushort replicationFactor)`: Create a Kafka topic.
- `Task DeleteTopicAsync(string name)`: Delete an existing Kafka topic.
- `void Dispose()`: Dispose of admin client resources.

### `IProsodyHandler<TPayload>`

Interface for handling messages and timers:

Implement all three methods. The compiler rejects an incomplete handler before subscription.

```csharp
public interface IProsodyHandler<TPayload>
{
    Task OnMessageAsync(ProsodyContext prosodyContext, Message<TPayload> message, CancellationToken cancellationToken);
    Task OnExciseAsync(ProsodyContext prosodyContext, ExciseMessage message, CancellationToken cancellationToken);
    Task OnTimerAsync(ProsodyContext prosodyContext, ProsodyTimer timer, CancellationToken cancellationToken);
}
```

### `Message<T>`

Represents a Kafka message with the following properties:

- `Topic` (string): The name of the topic.
- `Partition` (int): The partition number.
- `Offset` (long): The message offset within the partition.
- `Timestamp` (DateTimeOffset): The timestamp when the message was created or sent.
- `Key` (string): The message key.
- `T? Payload`: The deserialized payload (deserialized once before the handler is invoked).

### ProsodyContext

Represents the context of message processing:

- `bool ShouldCancel { get; }`: Check if cancellation has been requested (includes timeout and shutdown).
- `Task OnCancelAsync()`: Returns a task that completes when cancellation is signaled.

Keyed-state binding:

- `State(definition)`: Binds a registered collection for the current attempt, returning `IValueState<T>` / `IMapState<TValue>` / `IDequeState<T>` (message definitions vend `*State<Message<TPayload>>`). Throws `PermanentStateException` for an unregistered name or a kind/payload identity mismatch. See the [Keyed State](#keyed-state-2) API reference below.

Timer scheduling methods:

- `Task ScheduleAsync(DateTimeOffset time)`: Schedules a timer to fire at the specified time
- `Task ClearAndScheduleAsync(DateTimeOffset time)`: Clears all timers and schedules a new one
- `Task UnscheduleAsync(DateTimeOffset time)`: Removes a timer scheduled for the specified time
- `Task ClearScheduledAsync()`: Removes all scheduled timers
- `Task<DateTimeOffset[]> ScheduledAsync()`: Returns an array of all scheduled timer times

### ProsodyTimer

Represents a timer that has fired, provided to the `OnTimerAsync` method:

- `Key` (string): The entity key identifying what this timer belongs to
- `Time` (DateTimeOffset): The time when this timer was scheduled to fire

### ConsumerState

Enum representing the consumer lifecycle state:

- `Unconfigured`: Consumer has not been configured
- `Configured`: Consumer is configured but not running
- `Running`: Consumer is actively processing messages

### ClientMode

Enum representing the operating mode:

- `Pipeline`: Default mode, retry indefinitely with defer and monopolization detection
- `LowLatency`: Few retries then dead letter (requires FailureTopic)
- `BestEffort`: Log failures, no retries

### Keyed State

Definition factories (each returns an immutable, validated record used both in `WithStateCollections(...)` and with `context.State(...)`):

- `StateDefinition.Value<T>(string name, TimeSpan? ttl = null, bool? readUncommitted = null, bool published = false, StateReadCache? readCache = null)` → `ValueStateDefinition<T>`
- `StateDefinition.Map<TValue>(string name, TimeSpan? ttl = null, bool? readUncommitted = null, int? keysetLimit = null, bool published = false, StateReadCache? readCache = null)` → `MapStateDefinition<TValue>`
- `StateDefinition.Deque<T>(string name, TimeSpan? ttl = null, bool? readUncommitted = null, int? capacity = null, bool published = false, StateReadCache? readCache = null)` → `DequeStateDefinition<T>`
- `StateDefinition.MessageValue<TPayload>(string name, TimeSpan? ttl = null, bool? readUncommitted = null)` → `MessageValueDefinition<TPayload>`
- `StateDefinition.MessageMap<TPayload>(string name, TimeSpan? ttl = null, bool? readUncommitted = null, int? keysetLimit = null)` → `MessageMapDefinition<TPayload>`
- `StateDefinition.MessageDeque<TPayload>(string name, TimeSpan? ttl = null, bool? readUncommitted = null, int? capacity = null)` → `MessageDequeDefinition<TPayload>`

The item type parameter (`T` / `TValue`) uses `notnull` on JSON collections. Thus, a nullable item type causes a compile-time error.
Message collections use `Message<TPayload>`. Its payload can be null when `TPayload` permits a JSON null.

Published JSON collections use the same definition for owned and read-only access. See [Published state](#published-state) for setup and examples. `PublishedMap<TValue>` provides `GetAsync`, batched `GetManyAsync`, `ContainsKeyAsync`, `EnumerateAsync`, and key-only `EnumerateKeysAsync`. `PublishedDeque<T>` provides `GetAsync`, `CountAsync`, `IsEmptyAsync`, `PeekFrontAsync`, `PeekBackAsync`, and `EnumerateAsync`.

`IValueState<T> where T : notnull`:

- `Task<StateValue<T>> GetAsync(CancellationToken cancellationToken = default)`
- `Task SetAsync(T value, CancellationToken cancellationToken = default)`
- `Task ClearAsync(CancellationToken cancellationToken = default)`
- `Task CommitAsync(CancellationToken cancellationToken = default)`
- `Task RollbackAsync(CancellationToken cancellationToken = default)`

`IMapState<TValue> : IAsyncEnumerable<KeyValuePair<string, TValue>>` (keys are `string`, `TValue : notnull`):

- `Task<StateValue<TValue>> GetAsync(string key, CancellationToken cancellationToken = default)`
- `Task<IReadOnlyList<StateValue<TValue>>> GetManyAsync(IEnumerable<string> keys, CancellationToken cancellationToken = default)`
- `Task<bool> ContainsKeyAsync(string key, CancellationToken cancellationToken = default)`
- `Task SetAsync(string key, TValue value, CancellationToken cancellationToken = default)`
- `Task RemoveAsync(string key, CancellationToken cancellationToken = default)`
- `Task ClearAsync(CancellationToken cancellationToken = default)`
- `IAsyncEnumerable<KeyValuePair<string, TValue>> EnumerateAsync(ScanDirection direction = ScanDirection.Forward, CancellationToken cancellationToken = default)`
- `IAsyncEnumerable<string> EnumerateKeysAsync(ScanDirection direction = ScanDirection.Forward, CancellationToken cancellationToken = default)`
- `Task CommitAsync(CancellationToken cancellationToken = default)`
- `Task RollbackAsync(CancellationToken cancellationToken = default)`

`IDequeState<T> : IAsyncEnumerable<T>` (`T : notnull`):

- `Task PushBackAsync(T value, CancellationToken cancellationToken = default)`
- `Task PushFrontAsync(T value, CancellationToken cancellationToken = default)`
- `Task<StateValue<T>> PopFrontAsync(CancellationToken cancellationToken = default)`
- `Task<StateValue<T>> PopBackAsync(CancellationToken cancellationToken = default)`
- `Task<StateValue<T>> PeekFrontAsync(CancellationToken cancellationToken = default)`
- `Task<StateValue<T>> PeekBackAsync(CancellationToken cancellationToken = default)`
- `Task<StateValue<T>> GetAsync(int index, CancellationToken cancellationToken = default)`
- `Task<int> CountAsync(CancellationToken cancellationToken = default)`
- `Task<bool> IsEmptyAsync(CancellationToken cancellationToken = default)`
- `IAsyncEnumerable<T> EnumerateAsync(ScanDirection direction = ScanDirection.Forward, CancellationToken cancellationToken = default)`
- `Task CommitAsync(CancellationToken cancellationToken = default)`
- `Task RollbackAsync(CancellationToken cancellationToken = default)`

`StateValue<T>` (a `readonly struct` optional read result, `T : notnull`):

- `bool HasValue { get; }`: whether a value is present.
- `T Value { get; }`: the stored value; throws `InvalidOperationException` when absent.
- `T GetValueOrDefault(T defaultValue)`: the stored value when present, otherwise `defaultValue` (mirrors `Nullable<T>`).
- `bool TryGetValue(out T value)`: the familiar `Try` pattern — `true` with the value when present, `false` otherwise.

`ScanDirection`: `Forward` (ascending) or `Backward` (descending).

Errors:

- `StateException`: abstract base; exposes `StateErrorCategory Category { get; }`.
- `TransientStateException : StateException`: the default for a temporary store failure or caller mistake. Examples include a `null` write or invalid index.
- `NullValueException : TransientStateException`: a rejected `null`/unrepresentable write; use `ClearAsync` / `RemoveAsync` to delete instead.
- `PermanentStateException : StateException, IPermanentError`: reserved for failures a retry cannot resolve in-process (unregistered/identity-mismatched collection, duplicate/invalid name or TTL), or one a handler throws explicitly.
- `StateErrorCategory`: `Permanent` or `Transient`.

## License

MIT
