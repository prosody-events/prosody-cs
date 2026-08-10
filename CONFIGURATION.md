# Configuration

Configure via `ClientOptions` properties or environment variables. Properties take precedence; unset options (`null`) fall back to environment variables.

The .NET client reports values it cannot convert to Prosody types. Prosody validates configuration semantics when the client is built.

Common options have dedicated builder methods (e.g., `WithBootstrapServers()`). Set all other options via `Configure()` or directly on `ClientOptions`. See the [API reference](README.md#api-reference) for the full builder API.

## Dependency Injection

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

## JSON Serialization

Prosody serializes and deserializes payloads with these defaults:

- `PropertyNamingPolicy`: `CamelCase`
- `DefaultIgnoreCondition`: `WhenWritingNull`
- Converters: `JsonStringEnumConverter`

Override any option via `ConfigureJsonOptions`:

```csharp
ProsodyClientBuilder.Create()
    .ConfigureJsonOptions(opts =>
        opts.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower)
    .Build();
```

## AOT / Trim-safe Usage

By default, `new ProsodyClient(options)` and `ProsodyClientBuilder.Build()` install a `DefaultJsonTypeInfoResolver`,
which uses reflection. Both are annotated with `[RequiresUnreferencedCode]`/`[RequiresDynamicCode]`.

To eliminate trim/AOT warnings, supply a source-generated context and use the trim-clean overloads:

```csharp
[JsonSerializable(typeof(OrderCreated))]
[JsonSerializable(typeof(PaymentReceived))]
internal partial class AppJsonContext : JsonSerializerContext { }

// Register the source-gen context (replaces DefaultJsonTypeInfoResolver)
ProsodyClientBuilder.Create()
    .ConfigureJsonOptions(opts => opts.TypeInfoResolverChain.Add(AppJsonContext.Default))
    .Build();

// Trim-clean send: pass the JsonTypeInfo directly
var typeInfo = AppJsonContext.Default.OrderCreated;
await client.SendAsync(topic, key, order, typeInfo, cancellationToken);
```

| Path | AOT story |
|---|---|
| `SendAsync<T>(..., JsonTypeInfo<T>, ...)` | Fully trim-clean. |
| `SendAsync<T>(..., JsonTypeInfo<T>, SendOptions, ...)` | Fully trim-clean; explicit metadata bypasses naming-policy assumptions. |
| `SendAsync<T>(...)` (convenience) | Annotated; suppress `IL2026`/`IL3050` at call site if source-gen resolver is configured. |
| `new ProsodyClient(options)` / `Build()` | Annotated — installs `DefaultJsonTypeInfoResolver`. Suppress once at startup when using source-gen. |
| `SubscribeAsync<TPayload>(handler, classifier)` | Zero reflection for error classification — opt-in when you want full explicit control. Full AOT safety also requires the client's `JsonSerializerOptions` to use a source-gen resolver (via `ConfigureJsonOptions`) for payload deserialization. |
| `SubscribeAsync<TPayload>(handler)` | Annotated — reads `PermanentErrorAttribute` via `Type.GetInterfaceMap`. The BCL call is AOT-compatible, but the trimmer can't propagate DAM through an interface-typed parameter, so the method carries `[RequiresUnreferencedCode]`/`[RequiresDynamicCode]`. |

## Core

| Property / Environment Variable | Description | Default |
|---|---|---|
| `BootstrapServers` / `PROSODY_BOOTSTRAP_SERVERS` | Kafka servers to connect to | - |
| `GroupId` / `PROSODY_GROUP_ID` | Consumer group name | - |
| `SubscribedTopics` / `PROSODY_SUBSCRIBED_TOPICS` | Topics to read from | - |
| `AllowedEvents` / `PROSODY_ALLOWED_EVENTS` | Only process events matching these prefixes | (all) |
| `SourceSystem` / `PROSODY_SOURCE_SYSTEM` | Tag for outgoing messages (prevents reprocessing) | `<GroupId>` |
| `Mock` / `PROSODY_MOCK` | Use in-memory Kafka for testing | false |
| `Mode` / - | Processing mode: `Pipeline`, `LowLatency`, or `BestEffort` | `Pipeline` |
| - / `PROSODY_LOG` | Rust log filter, such as `info` or `prosody=debug` | `info` |

## Peer requests

Peer requests work with the defaults on one network. Set an advertised connect string only when another network cannot use the listener address.
Use a different bind address for each client that shares a host.

| Property / Environment Variable | Description | Default |
|---------------------------------|-------------|---------|
| `PeerBindAddress` / `PROSODY_PEER_BIND_ADDRESS` | Socket address for the peer gRPC listener | `0.0.0.0:9099` |
| `PeerAdvertisedConnect` / `PROSODY_PEER_ADVERTISED_CONNECT` | gRPC connect URI that peers on another network use | (none) |
| `PeerNetworkName` / `PROSODY_PEER_NETWORK_NAME` | Nonempty network name, with a maximum size of 63 UTF-8 bytes | (none) |
| `PeerCacheCapacity` / `PROSODY_PEER_CACHE_CAPACITY` | Maximum channels and peer records in each node-keyed cache | 256 |
| `PeerRegistrationTtl` / `PROSODY_PEER_REGISTRATION_TTL` | Directory lease duration; use 5 through 3600 seconds | 30s |

Set `Subsystem` to make this client answer requests. Without it, the client consumes messages but does not answer requests.

## Consumer

| Property / Environment Variable | Description | Default |
|---|---|---|
| `MaxConcurrency` / `PROSODY_MAX_CONCURRENCY` | Max messages being processed simultaneously | 32 |
| `MaxUncommitted` / `PROSODY_MAX_UNCOMMITTED` | Max queued messages before pausing consumption | 64 |
| `Timeout` / `PROSODY_TIMEOUT` | Cancel handler if it runs longer than this | 80% of stall threshold |
| `CommitInterval` / `PROSODY_COMMIT_INTERVAL` | How often to save progress to Kafka | 1s |
| `PollInterval` / `PROSODY_POLL_INTERVAL` | How often to fetch new messages from Kafka | 100ms |
| `ShutdownTimeout` / `PROSODY_SHUTDOWN_TIMEOUT` | Shutdown budget; handlers complete freely before cancellation fires near the deadline | 30s |
| `StallThreshold` / `PROSODY_STALL_THRESHOLD` | Report unhealthy if no progress for this long | 5m |
| `ProbePort` / `PROSODY_PROBE_PORT` | HTTP port for health checks; use `0` or the environment value `none` to disable | 8000 |
| - / `PROSODY_STATISTICS_INTERVAL` | How often librdkafka reports client statistics; must be between 1ms and 24h | 5s |
| `FailureTopic` / `PROSODY_FAILURE_TOPIC` | Send unprocessable messages here (dead letter queue) | - |
| `IdempotenceCacheSize` / `PROSODY_IDEMPOTENCE_CACHE_SIZE` | Global shared cache capacity across all partitions for message deduplication. Must be at least 1. | 8192 |
| `IdempotenceVersion` / `PROSODY_IDEMPOTENCE_VERSION` | Version string for cache-busting dedup hashes | 1 |
| `IdempotenceTtl` / `PROSODY_IDEMPOTENCE_TTL` | TTL for dedup records in Cassandra (minimum 1 minute) | 7 days |
| `SlabSize` / `PROSODY_SLAB_SIZE` | Timer storage granularity (rarely needs changing) | 1h |
| `MessageSpans` / `PROSODY_MESSAGE_SPANS` | Span linking for message execution: `child` (child-of) or `follows_from` | `child` |
| `TimerSpans` / `PROSODY_TIMER_SPANS` | Span linking for timer execution: `child` (child-of) or `follows_from` | `follows_from` |

## Producer

| Property / Environment Variable | Description | Default |
|---|---|---|
| `SendTimeout` / `PROSODY_SEND_TIMEOUT` | Give up sending after this long | 1s |

## Retry

Retry backoff applies in pipeline and low-latency modes. `MaxRetries` controls how many retries low-latency mode performs before routing the failure to `FailureTopic`. Pipeline mode uses deferral and does not use this limit.

| Property / Environment Variable | Description | Default |
|---|---|---|
| `MaxRetries` / `PROSODY_MAX_RETRIES` | Low-latency retries before routing to the failure topic | 3 |
| `RetryBase` / `PROSODY_RETRY_BASE` | Wait this long before first retry | 20ms |
| `MaxRetryDelay` / `PROSODY_RETRY_MAX_DELAY` | Never wait longer than this | 5m |

## Deferral (Pipeline Mode)

| Property / Environment Variable | Description | Default |
|---|---|---|
| `DeferEnabled` / `PROSODY_DEFER_ENABLED` | Enable deferral for new messages | true |
| `DeferBase` / `PROSODY_DEFER_BASE` | Wait this long before first deferred retry | 1s |
| `DeferMaxDelay` / `PROSODY_DEFER_MAX_DELAY` | Never wait longer than this | 24h |
| `DeferFailureThreshold` / `PROSODY_DEFER_FAILURE_THRESHOLD` | Disable deferral when failure rate exceeds this | 0.9 |
| `DeferFailureWindow` / `PROSODY_DEFER_FAILURE_WINDOW` | Measure failure rate over this time window | 5m |
| `DeferStoreCacheSize` / `PROSODY_DEFER_STORE_CACHE_SIZE` | Maximum deferred store cache entries per Cassandra defer store | 8192 |

## Kafka Message Loader (All Modes)

The shared loader resolves Kafka messages for deferral and keyed state:

| Property / Environment Variable | Description | Default |
|---|---|---|
| `LoaderCacheSize` / `PROSODY_LOADER_CACHE_SIZE` | Maximum messages retained by the shared Kafka loader | 1024 |
| `LoaderSeekTimeout` / `PROSODY_LOADER_SEEK_TIMEOUT` | Timeout for Kafka loader seek operations | 30s |
| `LoaderDiscardThreshold` / `PROSODY_LOADER_DISCARD_THRESHOLD` | Sequential-read distance before the loader seeks | 100 |

## Monopolization Detection (Pipeline Mode)

| Property / Environment Variable | Description | Default |
|---|---|---|
| `MonopolizationEnabled` / `PROSODY_MONOPOLIZATION_ENABLED` | Enable hot key protection | true |
| `MonopolizationThreshold` / `PROSODY_MONOPOLIZATION_THRESHOLD` | Max handler time as fraction of window | 0.9 |
| `MonopolizationWindow` / `PROSODY_MONOPOLIZATION_WINDOW` | Measurement window | 5m |
| `MonopolizationCacheSize` / `PROSODY_MONOPOLIZATION_CACHE_SIZE` | Max distinct keys to track | 8192 |

## Fair Scheduling (All Modes)

| Property / Environment Variable | Description | Default |
|---|---|---|
| `SchedulerFailureWeight` / `PROSODY_SCHEDULER_FAILURE_WEIGHT` | Fraction of processing time reserved for retries | 0.3 |
| `SchedulerMaxWait` / `PROSODY_SCHEDULER_MAX_WAIT` | Messages waiting this long get maximum priority | 2m |
| `SchedulerWaitWeight` / `PROSODY_SCHEDULER_WAIT_WEIGHT` | Priority boost for waiting messages (higher = more aggressive) | 200.0 |
| `SchedulerCacheSize` / `PROSODY_SCHEDULER_CACHE_SIZE` | Max distinct keys to track | 8192 |

## Telemetry

Prosody emits message, timer, and producer lifecycle events to a Kafka topic for observability:

| Property / Environment Variable | Description | Default |
|---|---|---|
| `TelemetryTopic` / `PROSODY_TELEMETRY_TOPIC` | Kafka topic for telemetry events | prosody.telemetry-events |
| `TelemetryEnabled` / `PROSODY_TELEMETRY_ENABLED` | Enable telemetry event emission | true |

Mock mode disables telemetry automatically, regardless of `TelemetryEnabled`.

## Cassandra

Persistent storage for timers, deferral, deduplication, and keyed state. It is not needed when `Mock = true`.

| Property / Environment Variable | Description | Default |
|---|---|---|
| `CassandraNodes` / `PROSODY_CASSANDRA_NODES` | Servers to connect to (host:port) | - |
| `CassandraKeyspace` / `PROSODY_CASSANDRA_KEYSPACE` | Keyspace name | prosody |
| `CassandraUser` / `PROSODY_CASSANDRA_USER` | Username | - |
| `CassandraPassword` / `PROSODY_CASSANDRA_PASSWORD` | Password | - |
| `CassandraDatacenter` / `PROSODY_CASSANDRA_DATACENTER` | Prefer this datacenter for queries | - |
| `CassandraRack` / `PROSODY_CASSANDRA_RACK` | Prefer this rack for queries | - |
| `CassandraRetention` / `PROSODY_CASSANDRA_RETENTION` | Delete data older than this | 1y |

## Keyed State

Register keyed-state collections before you subscribe, via `ProsodyClientBuilder.WithStateCollections(...)` or
`ClientOptions.StateCollections`. Persistence is backed by Cassandra and is not needed when `Mock = true`. See the
[Keyed State](README.md#keyed-state) for handler usage; the client-level knobs and per-collection fields are
below. Where an option and an environment variable are paired, an explicitly set option wins; otherwise the environment
variable applies, then the default.

| Property / Environment Variable | Description | Default |
|---|---|---|
| `StateCollections` / - | Collections to register before subscribe; duplicate names are rejected. Programmatic only (not IConfiguration-bindable). | (none) |
| `StateCacheDir` / `PROSODY_STATE_CACHE_DIR` | Disk workspace for the local keyed-state cache; each live client needs its own directory. Set a mounted path in production. | per-client temp dir |
| `StateOwnedCacheSize` / `PROSODY_STATE_OWNED_CACHE_SIZE` | Capacity of the owning keyed-state cache; accepts sizes such as `64 MiB` or `500 MB`. | storage-engine default |
| `StateReadCacheSize` / `PROSODY_STATE_READ_CACHE_SIZE` | Capacity of the published-state read cache; accepts sizes such as `1 MiB`. | `StateOwnedCacheSize` or `PROSODY_STATE_OWNED_CACHE_SIZE` when set; otherwise 1 MiB |
| `StateReadCache` / `PROSODY_STATE_READ_CACHE_TTL` | Default published-read cache policy. Use `StateReadCache.For(ttl)`, `StateReadCache.Disabled`, or the environment value `none`. | 5s |
| `Subsystem` / `PROSODY_SUBSYSTEM` | Subsystem name used to advertise JSON collections whose definitions set `published: true`. | (none) |
| `StateRecoveryDelay` / `PROSODY_STATE_RECOVERY_DELAY` | Delay between staging a provisional cell and the recovery sweep; every collection TTL must strictly exceed this. Whole seconds, min 1s. | 30s |

Declare each collection with a `StateDefinition` factory (`Value` / `Map` / `Deque` and their `Message*` variants).
The [API reference](README.md#api-reference) documents these factories. Their parameters map to these fields:

Published collections require `Subsystem`. Keep it configured for one deployment after removing `published: true` so readers can observe the collection's retirement.

| Option | Applies to | Description | Default |
|---|---|---|---|
| `name` | all | Collection name; non-empty and unique within the client. | (required) |
| `ttl` | all | Per-write TTL as a `TimeSpan`; whole seconds, `1..=630720000`, must exceed the recovery delay. | (none) |
| `published` | JSON | Advertises the owned collection for cross-group read-only access. | `false` |
| `readCache` | JSON | Per-reader cache override: `StateReadCache.For(ttl)` or `StateReadCache.Disabled`. | inherit |
| `readUncommitted` | all | Opt out of transactional staging (read-uncommitted). | false |
| `keysetLimit` | map only | Ordered-scan bound `0..=4096` (`0` disables ordered-scan tracking). | 128 |
| `capacity` | deque only | Maximum slot count (at least 1), enforced lazily on push. Runtime-only and may change across deploys. | unbounded |
