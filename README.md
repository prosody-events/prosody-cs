# Prosody: C# Bindings for Kafka

Prosody offers C# bindings to the [Prosody Kafka client](https://github.com/prosody-events/prosody), providing
features for message production and consumption, including configurable retry mechanisms, failure handling
strategies, and integrated OpenTelemetry support for distributed tracing.

## Features

- **Kafka Consumer**: Per-key ordering with cross-key concurrency, offset management, consumer groups
- **Kafka Producer**: Idempotent delivery with configurable retries
- **Timer System**: Persistent scheduled execution backed by Cassandra or in-memory store
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
await using var client = ProsodyClientBuilder.Create()
    // Bootstrap servers should normally be set using the PROSODY_BOOTSTRAP_SERVERS environment variable
    .WithBootstrapServers("localhost:9092")
    // To allow loopbacks, the SourceSystem must be different from the GroupId.
    // Normally, the SourceSystem would be left unspecified, which would default to the GroupId.
    .WithSourceSystem("my-application-source")
    // The GroupId should be set to the name of your application
    .WithGroupId("my-application")
    // Topics the client should subscribe to
    .WithSubscribedTopics("my-topic")
    .Build();

// Define a message handler
var messageHandler = new MyHandler();

// Subscribe to messages using the message handler
await client.SubscribeAsync(messageHandler);

// Send a message to a topic
await client.SendAsync("my-topic", "message-key", new { Content = "Hello, Kafka!" });

// Ensure proper shutdown when done
await client.UnsubscribeAsync();

// Handler implementation
public class MyHandler : IProsodyHandler
{
    public async Task OnMessageAsync(ProsodyContext prosodyContext, Message message, CancellationToken cancellationToken)
    {
        // Process the received message
        var payload = message.GetPayload<MyPayload>();
        Console.WriteLine($"Received message: {payload}");

        // Schedule a timer for delayed processing (requires Cassandra unless Mock = true)
        if (payload.ScheduleFollowup)
        {
            var futureTime = DateTimeOffset.UtcNow.AddSeconds(30);
            await prosodyContext.ScheduleAsync(futureTime);
        }
    }

    public async Task OnTimerAsync(ProsodyContext prosodyContext, Timer timer, CancellationToken cancellationToken)
    {
        // Handle timer firing
        Console.WriteLine($"Timer fired for key: {timer.Key} at {timer.Time}");
    }
}
```

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
await using var client = ProsodyClientBuilder.Create()
    .WithGroupId("my-consumer-group")
    .WithSubscribedTopics("my-topic")
    .Configure(options =>
    {
        options.DeferEnabled = true;                              // Enable deferral (default: true)
        options.DeferBase = TimeSpan.FromSeconds(1);              // Wait 1s before first retry
        options.DeferMaxDelay = TimeSpan.FromHours(24);           // Cap at 24 hours
        options.DeferFailureThreshold = 0.9;                      // Disable when >90% failing
    })
    .Build();
```

**Failure Rate Gating:** When >90% of recent messages fail, deferral disables. The retry middleware blocks the
partition, applying backpressure upstream.

### Monopolization Detection (Pipeline Mode)

Rejects keys that consume too much execution time.

The middleware tracks per-key execution time in 5-minute rolling windows. Keys exceeding 90% of window time are rejected
with a transient error, routing them through defer.

```csharp
// Configure monopolization detection
await using var client = ProsodyClientBuilder.Create()
    .WithGroupId("my-consumer-group")
    .WithSubscribedTopics("my-topic")
    .Configure(options =>
    {
        options.MonopolizationEnabled = true;                     // Enable detection (default: true)
        options.MonopolizationThreshold = 0.9;                    // Reject keys using >90% of window
        options.MonopolizationWindow = TimeSpan.FromMinutes(5);   // 5-minute window
    })
    .Build();
```

### Handler Timeout

Handlers are automatically cancelled if they exceed a deadline:

```csharp
await using var client = ProsodyClientBuilder.Create()
    .WithGroupId("my-consumer-group")
    .WithSubscribedTopics("my-topic")
    .Configure(options =>
    {
        options.Timeout = TimeSpan.FromSeconds(30);               // Cancel after 30 seconds
        options.StallThreshold = TimeSpan.FromSeconds(60);        // Report unhealthy after 60 seconds
    })
    .Build();
```

When a handler times out, `prosodyContext.ShouldCancel` becomes `true` and the `CancellationToken` is cancelled. The handler
should exit promptly. If not specified, timeout defaults to 80% of `StallThreshold`.

## Configuration

Configure via `ClientOptions` properties or environment variables. Properties take precedence; unset options (`null`) fall back to environment variables.

Common options have dedicated builder methods (e.g., `WithBootstrapServers()`). All other options are set via `Configure()` or directly on `ClientOptions`. See the [API Reference](#api-reference) for the full builder API.

### Dependency Injection

For ASP.NET Core or Generic Host applications, you can bind configuration using the options pipeline:

```csharp
var builder = WebApplication.CreateBuilder(args);

// Binds from the "Prosody" configuration section.
builder.Services.AddProsodyClient();

// Or bind from a custom section path:
builder.Services.AddProsodyClient("MySection:ProsodyConfig");

// Or apply programmatic overrides after binding:
builder.Services.AddProsodyClient(options => options.Mock = true);
```

The client is validated at startup via `ValidateOnStart()`. Invalid configuration throws `OptionsValidationException`.

### Core

| Property / Environment Variable | Description | Default |
|---|---|---|
| `BootstrapServers` / `PROSODY_BOOTSTRAP_SERVERS` | Kafka servers to connect to | - |
| `GroupId` / `PROSODY_GROUP_ID` | Consumer group name | - |
| `SubscribedTopics` / `PROSODY_SUBSCRIBED_TOPICS` | Topics to read from | - |
| `AllowedEvents` / `PROSODY_ALLOWED_EVENTS` | Only process events matching these prefixes | (all) |
| `SourceSystem` / `PROSODY_SOURCE_SYSTEM` | Tag for outgoing messages (prevents reprocessing) | `<GroupId>` |
| `Mock` / `PROSODY_MOCK` | Use in-memory Kafka for testing | false |

### Consumer

| Property / Environment Variable | Description | Default |
|---|---|---|
| `MaxConcurrency` / `PROSODY_MAX_CONCURRENCY` | Max messages being processed simultaneously | 32 |
| `MaxUncommitted` / `PROSODY_MAX_UNCOMMITTED` | Max queued messages before pausing consumption | 64 |
| `MaxEnqueuedPerKey` / `PROSODY_MAX_ENQUEUED_PER_KEY` | Max queued messages per key before pausing | 8 |
| `Timeout` / `PROSODY_TIMEOUT` | Cancel handler if it runs longer than this | 80% of stall threshold |
| `CommitInterval` / `PROSODY_COMMIT_INTERVAL` | How often to save progress to Kafka | 1s |
| `PollInterval` / `PROSODY_POLL_INTERVAL` | How often to fetch new messages from Kafka | 100ms |
| `ShutdownTimeout` / `PROSODY_SHUTDOWN_TIMEOUT` | Shutdown budget; handlers complete freely before cancellation fires near the deadline | 30s |
| `StallThreshold` / `PROSODY_STALL_THRESHOLD` | Report unhealthy if no progress for this long | 5m |
| `ProbePort` / `PROSODY_PROBE_PORT` | HTTP port for health checks (null=8000, 0=disabled) | 8000 |
| `FailureTopic` / `PROSODY_FAILURE_TOPIC` | Send unprocessable messages here (dead letter queue) | - |
| `IdempotenceCacheSize` / `PROSODY_IDEMPOTENCE_CACHE_SIZE` | Global shared cache capacity across all partitions for deduplication. Set to 0 to disable the entire deduplication middleware (both in-memory and persistent tiers) | 8192 |
| `IdempotenceVersion` / `PROSODY_IDEMPOTENCE_VERSION` | Version string for cache-busting dedup hashes | 1 |
| `IdempotenceTtl` / `PROSODY_IDEMPOTENCE_TTL` | TTL for dedup records in Cassandra (minimum 1 minute) | 7 days |
| `SlabSize` / `PROSODY_SLAB_SIZE` | Timer storage granularity (rarely needs changing) | 1h |
| `MessageSpans` / `PROSODY_MESSAGE_SPANS` | Span linking for message execution: `child` (child-of) or `follows_from` | `child` |
| `TimerSpans` / `PROSODY_TIMER_SPANS` | Span linking for timer execution: `child` (child-of) or `follows_from` | `follows_from` |

### Producer

| Property / Environment Variable | Description | Default |
|---|---|---|
| `SendTimeout` / `PROSODY_SEND_TIMEOUT` | Give up sending after this long | 1s |

### Retry

When a handler fails, retry with exponential backoff:

| Property / Environment Variable | Description | Default |
|---|---|---|
| `MaxRetries` / `PROSODY_MAX_RETRIES` | Give up after this many attempts | 3 |
| `RetryBase` / `PROSODY_RETRY_BASE` | Wait this long before first retry | 20ms |
| `MaxRetryDelay` / `PROSODY_RETRY_MAX_DELAY` | Never wait longer than this | 5m |

### Deferral (Pipeline Mode)

| Property / Environment Variable | Description | Default |
|---|---|---|
| `DeferEnabled` / `PROSODY_DEFER_ENABLED` | Enable deferral for new messages | true |
| `DeferBase` / `PROSODY_DEFER_BASE` | Wait this long before first deferred retry | 1s |
| `DeferMaxDelay` / `PROSODY_DEFER_MAX_DELAY` | Never wait longer than this | 24h |
| `DeferFailureThreshold` / `PROSODY_DEFER_FAILURE_THRESHOLD` | Disable deferral when failure rate exceeds this | 0.9 |
| `DeferFailureWindow` / `PROSODY_DEFER_FAILURE_WINDOW` | Measure failure rate over this time window | 5m |
| `DeferCacheSize` / `PROSODY_DEFER_CACHE_SIZE` | Track this many deferred keys in memory | 1024 |
| `DeferSeekTimeout` / `PROSODY_DEFER_SEEK_TIMEOUT` | Timeout when loading deferred messages | 30s |
| `DeferDiscardThreshold` / `PROSODY_DEFER_DISCARD_THRESHOLD` | Read optimization (rarely needs changing) | 100 |

### Monopolization Detection (Pipeline Mode)

| Property / Environment Variable | Description | Default |
|---|---|---|
| `MonopolizationEnabled` / `PROSODY_MONOPOLIZATION_ENABLED` | Enable hot key protection | true |
| `MonopolizationThreshold` / `PROSODY_MONOPOLIZATION_THRESHOLD` | Max handler time as fraction of window | 0.9 |
| `MonopolizationWindow` / `PROSODY_MONOPOLIZATION_WINDOW` | Measurement window | 5m |
| `MonopolizationCacheSize` / `PROSODY_MONOPOLIZATION_CACHE_SIZE` | Max distinct keys to track | 8192 |

### Fair Scheduling (All Modes)

| Property / Environment Variable | Description | Default |
|---|---|---|
| `SchedulerFailureWeight` / `PROSODY_SCHEDULER_FAILURE_WEIGHT` | Fraction of processing time reserved for retries | 0.3 |
| `SchedulerMaxWait` / `PROSODY_SCHEDULER_MAX_WAIT` | Messages waiting this long get maximum priority | 2m |
| `SchedulerWaitWeight` / `PROSODY_SCHEDULER_WAIT_WEIGHT` | Priority boost for waiting messages (higher = more aggressive) | 200.0 |
| `SchedulerCacheSize` / `PROSODY_SCHEDULER_CACHE_SIZE` | Max distinct keys to track | 8192 |

### Cassandra

Persistent storage for timers and deferred retries (not needed if `Mock = true`):

| Property / Environment Variable | Description | Default |
|---|---|---|
| `CassandraNodes` / `PROSODY_CASSANDRA_NODES` | Servers to connect to (host:port) | - |
| `CassandraKeyspace` / `PROSODY_CASSANDRA_KEYSPACE` | Keyspace name | prosody |
| `CassandraUser` / `PROSODY_CASSANDRA_USER` | Username | - |
| `CassandraPassword` / `PROSODY_CASSANDRA_PASSWORD` | Password | - |
| `CassandraDatacenter` / `PROSODY_CASSANDRA_DATACENTER` | Prefer this datacenter for queries | - |
| `CassandraRack` / `PROSODY_CASSANDRA_RACK` | Prefer this rack for queries | - |
| `CassandraRetention` / `PROSODY_CASSANDRA_RETENTION` | Delete data older than this | 1y |

### Telemetry

Lifecycle event emission to a Kafka topic (message dispatched, succeeded, failed; timer scheduled, etc.):

| Property / Environment Variable | Description | Default |
|---|---|---|
| `TelemetryTopic` / `PROSODY_TELEMETRY_TOPIC` | Kafka topic for telemetry events | prosody.telemetry-events |
| `TelemetryEnabled` / `PROSODY_TELEMETRY_ENABLED` | Enable telemetry event emission | true |

## Liveness and Readiness Probes

Prosody includes a built-in probe server for consumer-based applications that provides health check endpoints. The probe
server is tied to the consumer's lifecycle and offers two main endpoints:

1. `/readyz`: A readiness probe that checks if any partitions are assigned to the consumer. Returns a success status
   only when the consumer has at least one partition assigned, indicating it's ready to process messages.

2. `/livez`: A liveness probe that checks if any partitions have stalled (haven't processed a message within a
   configured time threshold).

Configure the probe server using the builder:

```csharp
await using var client = ProsodyClientBuilder.Create()
    .WithGroupId("my-consumer-group")
    .WithSubscribedTopics("my-topic")
    .WithProbePort(8000)                                          // Set to 0 to disable
    .Configure(options =>
    {
        options.StallThreshold = TimeSpan.FromSeconds(15);        // 15 seconds before considering a partition stalled
    })
    .Build();
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

## Advanced Usage

### Pipeline Mode

Pipeline mode is the default mode. Ensures ordered processing, retrying failed operations indefinitely:

```csharp
// Initialize client in pipeline mode
await using var client = ProsodyClientBuilder.Create()
    .WithMode(ClientMode.Pipeline)  // Explicitly set pipeline mode (this is the default)
    .WithGroupId("my-consumer-group")
    .WithSubscribedTopics("my-topic")
    .Build();
```

### Low-Latency Mode

Prioritizes quick processing, sending persistently failing messages to a failure topic:

```csharp
// Initialize client in low-latency mode
await using var client = ProsodyClientBuilder.Create()
    .WithMode(ClientMode.LowLatency)  // Set low-latency mode
    .WithGroupId("my-consumer-group")
    .WithSubscribedTopics("my-topic")
    .WithFailureTopic("failed-messages")  // Specify a topic for failed messages
    .Build();
```

### Best-Effort Mode

Optimized for development environments or services where message processing failures are acceptable:

```csharp
// Initialize client in best-effort mode
await using var client = ProsodyClientBuilder.Create()
    .WithMode(ClientMode.BestEffort)  // Set best-effort mode
    .WithGroupId("my-consumer-group")
    .WithSubscribedTopics("my-topic")
    .Build();
```

## Event Type Filtering

Prosody supports filtering messages based on event type prefixes, allowing your consumer to process only specific types
of events:

```csharp
// Process only events with types starting with "user." or "account."
await using var client = ProsodyClientBuilder.Create()
    .WithGroupId("my-consumer-group")
    .WithSubscribedTopics("my-topic")
    .WithAllowedEvents("user.", "account.")
    .Build();
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
await using var client = ProsodyClientBuilder.Create()
    .WithGroupId("my-service")
    .WithSourceSystem("my-service-producer")  // Must differ from GroupId to allow loopbacks; defaults to GroupId
    .WithSubscribedTopics("my-topic")
    .Build();
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

The entire deduplication middleware (both in-memory and persistent tiers) can be disabled by setting `IdempotenceCacheSize = 0`:

```csharp
await using var client = ProsodyClientBuilder.Create()
    .WithGroupId("my-consumer-group")
    .WithSubscribedTopics("my-topic")
    .Configure(options => options.IdempotenceCacheSize = 0)       // Disable deduplication
    .Build();
```

Or via environment variable:

```bash
PROSODY_IDEMPOTENCE_CACHE_SIZE=0
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
public class MyHandler : IProsodyHandler
{
    public async Task OnMessageAsync(ProsodyContext prosodyContext, Message message, CancellationToken cancellationToken)
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

    public async Task OnTimerAsync(ProsodyContext prosodyContext, Timer timer, CancellationToken cancellationToken)
    {
        Console.WriteLine("Timer fired!");
        Console.WriteLine($"Key: {timer.Key}");
        Console.WriteLine($"Scheduled time: {timer.Time}");
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
await using var client = ProsodyClientBuilder.Create()
    .WithBootstrapServers("localhost:9092")
    .WithGroupId("my-application")
    .WithSubscribedTopics("my-topic")
    .Configure(options => options.CassandraNodes = ["localhost:9042"])  // Required unless Mock = true
    .Build();
```

For testing, you can use mock mode to avoid Cassandra dependency:

```csharp
// Mock mode for testing (timers work but aren't persisted)
await using var client = ProsodyClientBuilder.Create()
    .WithBootstrapServers("localhost:9092")
    .WithGroupId("my-application")
    .WithSubscribedTopics("my-topic")
    .WithMock(true)  // No Cassandra required in mock mode
    .Build();
```

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

public class MyHandler : IProsodyHandler
{
    private static readonly ActivitySource ActivitySource = new("my-service-name");

    public async Task OnMessageAsync(ProsodyContext prosodyContext, Message message, CancellationToken cancellationToken)
    {
        using var activity = ActivitySource.StartActivity("process-message");

        // Process the received message
        activity?.AddEvent(new ActivityEvent("message.received"));

        var payload = message.GetPayload<MyPayload>();
        Console.WriteLine($"Received message: {payload}");
    }

    public Task OnTimerAsync(ProsodyContext prosodyContext, Timer timer, CancellationToken cancellationToken) => Task.CompletedTask;
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

Always unsubscribe from topics before exiting your application:

```csharp
// Ensure proper shutdown
await client.UnsubscribeAsync();
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
    private readonly ProsodyClient _client;

    public ProsodyWorker()
    {
        _client = ProsodyClientBuilder.Create()
            .WithBootstrapServers("localhost:9092")
            .WithGroupId("my-consumer-group")
            .WithSubscribedTopics("my-topic")
            .Build();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await _client.SubscribeAsync(new MyHandler());

        // Wait for shutdown signal
        await Task.Delay(Timeout.Infinite, stoppingToken);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        Console.WriteLine("Shutting down gracefully...");
        await _client.UnsubscribeAsync();
        _client.Dispose();
        await base.StopAsync(cancellationToken);
    }
}
```

### Error Handling

Prosody classifies errors as transient (temporary, can be retried) or permanent (won't be resolved by retrying). By
default, all errors are considered transient.

#### Using Attributes

Use the `[PermanentError]` attribute to classify exceptions that should not be retried:

```csharp
using Prosody;
using System.Text.Json;

public class MyHandler : IProsodyHandler
{
    [PermanentError(typeof(JsonException), typeof(ArgumentException))]
    public async Task OnMessageAsync(ProsodyContext prosodyContext, Message message, CancellationToken cancellationToken)
    {
        // Your message handling logic here
        // JsonException and ArgumentException will be treated as permanent
        // All other exceptions will be treated as transient (default behavior)
    }

    public Task OnTimerAsync(ProsodyContext prosodyContext, Timer timer, CancellationToken cancellationToken) => Task.CompletedTask;
}
```

#### Using PermanentException

You can also throw a `PermanentException` directly:

```csharp
using Prosody;

public class MyHandler : IProsodyHandler
{
    public async Task OnMessageAsync(ProsodyContext prosodyContext, Message message, CancellationToken cancellationToken)
    {
        var payload = message.GetPayload<MyPayload>();

        if (payload.Version < MinimumSupportedVersion)
        {
            throw new PermanentException("Message version is no longer supported");
        }

        // Process message...
    }

    public Task OnTimerAsync(ProsodyContext prosodyContext, Timer timer, CancellationToken cancellationToken) => Task.CompletedTask;
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
public class MyHandler : IProsodyHandler
{
    private readonly HttpClient _httpClient;
    private readonly MyDbContext _dbContext;
    private readonly ProsodyClient _client;

    public async Task OnMessageAsync(ProsodyContext prosodyContext, Message message, CancellationToken cancellationToken)
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

    public Task OnTimerAsync(ProsodyContext prosodyContext, Timer timer, CancellationToken cancellationToken) => Task.CompletedTask;
}
```

For CPU-bound loops, poll `ThrowIfCancellationRequested()` periodically. This throws `OperationCanceledException` when
cancellation is requested, correctly signaling to Prosody that the handler did not complete:

```csharp
public async Task OnMessageAsync(ProsodyContext prosodyContext, Message message, CancellationToken cancellationToken)
{
    var items = message.GetPayload<List<Item>>();

    foreach (var item in items)
    {
        // Correct: throws OperationCanceledException, signaling incomplete work
        cancellationToken.ThrowIfCancellationRequested();

        ProcessItem(item);
    }
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

var host = builder.Build();
```

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

**Build:**
- `ProsodyClient Build()`: Validates configuration and creates a new ProsodyClient.

### ProsodyClient

- `ProsodyClient(ClientOptions options)`: Create a new ProsodyClient with the specified options.
- `string SourceSystem { get; }`: Get the source system identifier configured for the client.
- `Task<ConsumerState> ConsumerStateAsync()`: Get the current state of the consumer.
- `Task<uint> AssignedPartitionCountAsync()`: Get the number of partitions currently assigned to this consumer.
- `Task<bool> IsStalledAsync()`: Check if the consumer has stalled partitions.
- `Task SendAsync<T>(string topic, string key, T payload, CancellationToken cancellationToken = default)`: Send a message to a specified topic.
- `Task SendRawAsync(string topic, string key, byte[] jsonPayload, CancellationToken cancellationToken = default)`: Send raw JSON bytes to a specified topic.
- `Task SubscribeAsync(IProsodyHandler handler)`: Subscribe to messages using the provided handler.
- `Task UnsubscribeAsync()`: Unsubscribe from messages and shut down the consumer.
- `void Dispose()`: Dispose of client resources synchronously.
- `ValueTask DisposeAsync()`: Dispose of client resources asynchronously (unsubscribes the consumer first). Enables `await using`.

### AdminClient

- `AdminClient(params string[] bootstrapServers)`: Initialize a new AdminClient with the given configuration.
- `Task CreateTopicAsync(string name, ushort partitionCount, ushort replicationFactor)`: Create a Kafka topic.
- `Task DeleteTopicAsync(string name)`: Delete an existing Kafka topic.
- `void Dispose()`: Dispose of admin client resources.

### IProsodyHandler

Interface for handling messages and timers:

```csharp
public interface IProsodyHandler
{
    Task OnMessageAsync(ProsodyContext prosodyContext, Message message, CancellationToken cancellationToken);
    Task OnTimerAsync(ProsodyContext prosodyContext, Timer timer, CancellationToken cancellationToken);
}
```

### Message

Represents a Kafka message with the following properties:

- `Topic` (string): The name of the topic.
- `Partition` (int): The partition number.
- `Offset` (long): The message offset within the partition.
- `Timestamp` (DateTimeOffset): The timestamp when the message was created or sent.
- `Key` (string): The message key.
- `T GetPayload<T>()`: Deserialize and return the message payload as type T.

### ProsodyContext

Represents the context of message processing:

- `bool ShouldCancel { get; }`: Check if cancellation has been requested (includes timeout and shutdown).
- `Task OnCancelAsync()`: Returns a task that completes when cancellation is signaled.

Timer scheduling methods:

- `Task ScheduleAsync(DateTimeOffset time)`: Schedules a timer to fire at the specified time
- `Task ClearAndScheduleAsync(DateTimeOffset time)`: Clears all timers and schedules a new one
- `Task UnscheduleAsync(DateTimeOffset time)`: Removes a timer scheduled for the specified time
- `Task ClearScheduledAsync()`: Removes all scheduled timers
- `Task<DateTimeOffset[]> ScheduledAsync()`: Returns an array of all scheduled timer times

### Timer

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

## License

MIT
