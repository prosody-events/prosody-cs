//! Configuration conversion utilities for FFI bindings.
//!
//! This module converts [`ClientOptions`] into the various prosody builder
//! types needed to construct a [`prosody::high_level::HighLevelClient`].
//!
//! # Design Pattern
//!
//! Following sibling wrappers (prosody-js, prosody-py, prosody-rb):
//!
//! - Builder fields are only set when the corresponding option is `Some`
//! - `None` values allow builder defaults and environment variable fallbacks to
//!   apply
//! - Most functions are infallible; validation happens when builders are
//!   finalized. The exceptions finalize a nested sub-configuration eagerly:
//!   [`build_consumer_config`] builds the Kafka loader tuning and
//!   [`build_dedup_config`] converts the deduplication cache capacity, both of
//!   which can reject invalid caller input.

use prosody::cassandra::config::CassandraConfigurationBuilder;
use prosody::codec::{JsonBinaryCodec, JsonPassthroughStateCodec};
use prosody::consumer::ConsumerConfigurationBuilder;
use prosody::consumer::KeyedStateConfiguration;
use prosody::consumer::SpanRelation as ProsodySpanRelation;
use prosody::consumer::kafka_state::{message_deque_state, message_map_state, message_state};
use prosody::consumer::middleware::deduplication::DeduplicationConfigurationBuilder;
use prosody::consumer::middleware::defer::DeferConfigurationBuilder;
use prosody::consumer::middleware::monopolization::MonopolizationConfigurationBuilder;
use prosody::consumer::middleware::retry::RetryConfigurationBuilder;
use prosody::consumer::middleware::scheduler::SchedulerConfigurationBuilder;
use prosody::consumer::middleware::timeout::TimeoutConfigurationBuilder;
use prosody::consumer::middleware::topic::FailureTopicConfigurationBuilder;
use prosody::high_level::ConsumerBuilders;
use prosody::high_level::mode::Mode;
use prosody::loader::KafkaLoader;
use prosody::loader::KafkaLoaderConfiguration;
use prosody::producer::ProducerConfigurationBuilder;
use prosody::state::descriptor::{
    DequeDescriptor, MapDescriptor, StateDescriptor, deque_state, map_state, value_state,
};
use prosody::state::order_codec::Utf8KeyCodec;
use prosody::subsystem::SubsystemName;
use prosody::telemetry::emitter::{
    TelemetryEmitterConfiguration, TelemetryEmitterConfigurationBuilder,
};
use prosody::timers::duration::CompactDuration;
use std::collections::HashSet;
use std::num::{NonZeroU64, NonZeroUsize};
use std::path::PathBuf;
use std::time::Duration;
use validator::{ValidationError, ValidationErrors};

use crate::error::FfiError;
use crate::types::{
    ClientMode, ClientOptions, SpanRelation, StateCollectionConfig, StateKind, StatePayload,
};

/// Creates a producer configuration builder from client options.
///
/// Configures Kafka producer settings including bootstrap servers, mock mode,
/// source system identifier, and send timeout.
#[must_use]
pub fn build_producer_config(options: &ClientOptions) -> ProducerConfigurationBuilder {
    let mut builder = ProducerConfigurationBuilder::default();

    if let Some(servers) = &options.bootstrap_servers {
        builder.bootstrap_servers(servers.clone());
    }

    if let Some(mock) = options.mock {
        builder.mock(mock);
    }

    if let Some(source_system) = &options.source_system {
        builder.source_system(source_system);
    }

    if let Some(timeout) = options.send_timeout {
        builder.send_timeout(Some(timeout));
    }

    builder
}

/// Creates a consumer configuration builder from client options.
///
/// Configures Kafka consumer settings including bootstrap servers, group ID,
/// topic subscriptions, and flow control parameters.
///
/// # Probe Port Handling
///
/// The `probe_port` option uses special semantics:
/// - `None`: Use builder default (typically enabled with auto-assigned port)
/// - `Some(0)`: Explicitly disable the probe endpoint
/// - `Some(1..=65535)`: Use the specified port number
///
/// # Errors
///
/// Returns [`FfiError::LoaderConfig`] if the deferred-retry loader tuning
/// derived from `defer_cache_size`, `defer_seek_timeout`, or
/// `defer_discard_threshold` fails validation.
pub fn build_consumer_config(
    options: &ClientOptions,
) -> Result<ConsumerConfigurationBuilder, FfiError> {
    let mut builder = ConsumerConfigurationBuilder::default();

    if let Some(servers) = &options.bootstrap_servers {
        builder.bootstrap_servers(servers.clone());
    }

    if let Some(mock) = options.mock {
        builder.mock(mock);
    }

    if let Some(group_id) = &options.group_id {
        builder.group_id(group_id);
    }

    if let Some(topics) = &options.subscribed_topics {
        builder.subscribed_topics(topics.clone());
    }

    if let Some(allowed_events) = &options.allowed_events {
        builder.allowed_events(allowed_events.clone());
    }

    if let Some(max_uncommitted) = options.max_uncommitted {
        builder.max_uncommitted(max_uncommitted as usize);
    }

    if let Some(stall_threshold) = options.stall_threshold {
        builder.stall_threshold(stall_threshold);
    }

    if let Some(shutdown_timeout) = options.shutdown_timeout {
        builder.shutdown_timeout(shutdown_timeout);
    }

    if let Some(poll_interval) = options.poll_interval {
        builder.poll_interval(poll_interval);
    }

    if let Some(commit_interval) = options.commit_interval {
        builder.commit_interval(commit_interval);
    }

    if let Some(probe_port) = options.probe_port {
        if probe_port == 0 {
            builder.probe_port(None);
        } else {
            builder.probe_port(Some(probe_port));
        }
    }

    if let Some(slab_size) = options.slab_size {
        builder.slab_size(slab_size);
    }

    if let Some(message_spans) = options.message_spans {
        builder.message_spans(match message_spans {
            SpanRelation::Child => ProsodySpanRelation::Child,
            SpanRelation::FollowsFrom => ProsodySpanRelation::FollowsFrom,
        });
    }

    if let Some(timer_spans) = options.timer_spans {
        builder.timer_spans(match timer_spans {
            SpanRelation::Child => ProsodySpanRelation::Child,
            SpanRelation::FollowsFrom => ProsodySpanRelation::FollowsFrom,
        });
    }

    // The deferred-retry message loader tuning is consumer-wide in the current
    // core API. Only build and attach it when the caller supplied at least one
    // loader knob, so the default configuration keeps environment fallbacks.
    if options.defer_cache_size.is_some()
        || options.defer_seek_timeout.is_some()
        || options.defer_discard_threshold.is_some()
    {
        let mut loader = KafkaLoaderConfiguration::builder();

        if let Some(cache_size) = options.defer_cache_size {
            loader.cache_size(cache_size as usize);
        }

        if let Some(seek_timeout) = options.defer_seek_timeout {
            loader.seek_timeout(seek_timeout);
        }

        if let Some(discard_threshold) = options.defer_discard_threshold {
            loader.discard_threshold(i64::from(discard_threshold));
        }

        builder.loader(loader.build()?);
    }

    Ok(builder)
}

/// Creates a retry configuration builder from client options.
///
/// Configures exponential backoff retry behavior with base delay, maximum
/// retry count, and maximum delay cap.
#[must_use]
pub fn build_retry_config(options: &ClientOptions) -> RetryConfigurationBuilder {
    let mut builder = RetryConfigurationBuilder::default();

    if let Some(base) = options.retry_base {
        builder.base(base);
    }

    if let Some(max_retries) = options.max_retries {
        builder.max_retries(max_retries);
    }

    if let Some(max_delay) = options.max_retry_delay {
        builder.max_delay(max_delay);
    }

    builder
}

/// Creates a failure topic configuration builder from client options.
///
/// Configures the dead-letter topic where messages are sent after exhausting
/// all retry attempts.
#[must_use]
pub fn build_failure_topic_config(options: &ClientOptions) -> FailureTopicConfigurationBuilder {
    let mut builder = FailureTopicConfigurationBuilder::default();

    if let Some(topic) = &options.failure_topic {
        builder.failure_topic(topic);
    }

    builder
}

/// Creates a scheduler configuration builder from client options.
///
/// Configures the message scheduler which controls concurrency limits, failure
/// weighting for adaptive throttling, and wait time parameters.
#[must_use]
pub fn build_scheduler_config(options: &ClientOptions) -> SchedulerConfigurationBuilder {
    let mut builder = SchedulerConfigurationBuilder::default();

    if let Some(max_concurrency) = options.max_concurrency {
        builder.max_concurrency(max_concurrency as usize);
    }

    if let Some(failure_weight) = options.scheduler_failure_weight {
        builder.failure_weight(failure_weight);
    }

    if let Some(max_wait) = options.scheduler_max_wait {
        builder.max_wait(max_wait);
    }

    if let Some(wait_weight) = options.scheduler_wait_weight {
        builder.wait_weight(wait_weight);
    }

    if let Some(cache_size) = options.scheduler_cache_size {
        builder.cache_size(cache_size as usize);
    }

    builder
}

/// Creates a monopolization configuration builder from client options.
///
/// Configures monopolization detection which prevents a single message key
/// from consuming excessive processing capacity within a time window.
#[must_use]
pub fn build_monopolization_config(options: &ClientOptions) -> MonopolizationConfigurationBuilder {
    let mut builder = MonopolizationConfigurationBuilder::default();

    if let Some(enabled) = options.monopolization_enabled {
        builder.enabled(enabled);
    }

    if let Some(threshold) = options.monopolization_threshold {
        builder.monopolization_threshold(threshold);
    }

    if let Some(window) = options.monopolization_window {
        builder.window_duration(window);
    }

    if let Some(cache_size) = options.monopolization_cache_size {
        builder.cache_size(cache_size as usize);
    }

    builder
}

/// Creates a defer configuration builder from client options.
///
/// Configures the defer middleware which delays reprocessing of messages
/// from keys that have experienced recent failures, using exponential backoff.
#[must_use]
pub fn build_defer_config(options: &ClientOptions) -> DeferConfigurationBuilder {
    let mut builder = DeferConfigurationBuilder::default();

    if let Some(enabled) = options.defer_enabled {
        builder.enabled(enabled);
    }

    if let Some(base) = options.defer_base {
        builder.base(base);
    }

    if let Some(max_delay) = options.defer_max_delay {
        builder.max_delay(max_delay);
    }

    if let Some(failure_threshold) = options.defer_failure_threshold {
        builder.failure_threshold(failure_threshold);
    }

    if let Some(failure_window) = options.defer_failure_window {
        builder.failure_window(failure_window);
    }

    if let Some(store_cache_size) = options.defer_store_cache_size {
        builder.store_cache_size(store_cache_size as usize);
    }

    builder
}

/// Creates a timeout configuration builder from client options.
///
/// Configures the per-message processing timeout after which handlers are
/// cancelled and the message is marked as failed.
#[must_use]
pub fn build_timeout_config(options: &ClientOptions) -> TimeoutConfigurationBuilder {
    let mut builder = TimeoutConfigurationBuilder::default();

    if let Some(timeout) = options.timeout {
        builder.timeout(Some(timeout));
    }

    builder
}

/// Creates a deduplication configuration builder from client options.
///
/// Configures the deduplication middleware including the global shared cache
/// capacity, version string for cache-busting, and Cassandra TTL.
///
/// # Errors
///
/// Returns [`FfiError::Validation`] if `idempotence_cache_size` is set to zero;
/// the current core API models the deduplication cache capacity as a non-zero
/// value, so a zero capacity is rejected rather than silently ignored.
pub fn build_dedup_config(
    options: &ClientOptions,
) -> Result<DeduplicationConfigurationBuilder, FfiError> {
    let mut builder = DeduplicationConfigurationBuilder::default();

    if let Some(cache_capacity) = options.idempotence_cache_size {
        let cache_capacity = NonZeroUsize::new(cache_capacity as usize).ok_or_else(|| {
            let mut errors = ValidationErrors::new();
            errors.add(
                "idempotence_cache_size",
                ValidationError::new("idempotence_cache_size_must_be_non_zero"),
            );
            FfiError::Validation(errors)
        })?;
        builder.cache_capacity(cache_capacity);
    }

    if let Some(version) = &options.idempotence_version {
        builder.version(version.clone());
    }

    if let Some(ttl) = options.idempotence_ttl {
        builder.ttl(ttl);
    }

    Ok(builder)
}

/// Creates a telemetry emitter configuration builder from client options.
///
/// Configures the background Kafka emitter that publishes message and timer
/// lifecycle events to a dedicated telemetry topic.
#[must_use]
pub fn build_telemetry_emitter_config(
    options: &ClientOptions,
) -> TelemetryEmitterConfigurationBuilder {
    let mut builder = TelemetryEmitterConfiguration::builder();

    if let Some(topic) = &options.telemetry_topic {
        builder.topic(topic.clone());
    }

    if let Some(enabled) = options.telemetry_enabled {
        builder.enabled(enabled);
    }

    builder
}

/// The inclusive upper bound core accepts for a map's keyset limit.
const MAX_KEYSET_LIMIT: u32 = 4096;

/// Builds a permanent state error for an invalid keyed-state configuration.
///
/// Configuration and deployment mistakes are permanent: retrying an
/// unregisterable collection cannot succeed, so the error must not be retried.
fn permanent_config(message: String) -> FfiError {
    FfiError::PermanentState(message)
}

/// Validates a duration as a whole number of seconds of at least `min`.
///
/// The field arrives as a [`Duration`] (a C# `TimeSpan`) so that fractional
/// (sub-second) and out-of-range values reach this guard rather than being
/// silently truncated by a `u32` conversion. A sub-second component or a value
/// outside `min..=u32::MAX` seconds is rejected with a permanent error naming
/// the field.
///
/// # Errors
///
/// Returns [`FfiError::PermanentState`] if the duration is not a whole number
/// of seconds in `min..=u32::MAX`.
fn whole_seconds(duration: Duration, field: &str, min: u32) -> Result<u32, FfiError> {
    if duration.subsec_nanos() != 0 {
        return Err(permanent_config(format!(
            "{field}: must be a whole number of seconds"
        )));
    }
    let seconds = duration.as_secs();
    if seconds < u64::from(min) || seconds > u64::from(u32::MAX) {
        return Err(permanent_config(format!(
            "{field}: must be between {min} and {} seconds",
            u32::MAX
        )));
    }
    Ok(seconds as u32)
}

/// Applies the shared descriptor options (TTL, commit mode) fluently.
fn with_def<D: StateDescriptor>(
    descriptor: D,
    ttl_seconds: Option<u32>,
    read_uncommitted: Option<bool>,
    published: bool,
) -> D {
    let mut descriptor = descriptor;
    if let Some(ttl) = ttl_seconds {
        descriptor = descriptor.ttl(CompactDuration::new(ttl));
    }
    if read_uncommitted == Some(true) {
        descriptor = descriptor.read_uncommitted();
    }
    descriptor = descriptor.published(published);
    descriptor
}

/// Applies the map-only keyset bound when configured.
fn with_keyset<KC, V>(
    descriptor: MapDescriptor<KC, V>,
    keyset_limit: Option<u32>,
) -> MapDescriptor<KC, V> {
    match keyset_limit {
        Some(limit) => descriptor.keyset_limit(limit as usize),
        None => descriptor,
    }
}

/// Applies the deque-only capacity bound when configured.
fn with_capacity<T>(
    descriptor: DequeDescriptor<T>,
    capacity: Option<NonZeroUsize>,
) -> DequeDescriptor<T> {
    match capacity {
        Some(cap) => descriptor.capacity(cap),
        None => descriptor,
    }
}

/// Validates one collection and registers its descriptor.
///
/// JSON collections monomorphize over the
/// [`BinaryPayload`](prosody::codec::BinaryPayload) passthrough codec (Rust
/// never parses the JSON bytes) and claim the shared `"json"` format id.
/// Message collections monomorphize over `KafkaLoader<JsonBinaryCodec>`, the
/// consumer's own codec, but their stored identity is loader-independent (the
/// message-ref codec and resolver carry the fixed `"message-ref"` identifiers),
/// so registering with this loader matches the identity the erased vend path
/// asserts using the session's own loader.
///
/// # Errors
///
/// Returns [`FfiError::PermanentState`] if a field is invalid (empty name, TTL
/// not a whole number of seconds, keyset limit out of range or set on a
/// non-map collection, capacity zero or set on a non-deque collection).
fn register_state_collection(
    keyed: &mut KeyedStateConfiguration,
    index: usize,
    collection: &StateCollectionConfig,
) -> Result<(), FfiError> {
    if collection.name.is_empty() {
        return Err(permanent_config(format!(
            "stateCollections[{index}].name: must not be empty"
        )));
    }

    let ttl_seconds = match collection.ttl {
        Some(ttl) => Some(whole_seconds(
            ttl,
            &format!("stateCollections[{index}].ttl"),
            1,
        )?),
        None => None,
    };

    let (keyset_limit, capacity) = collection_bounds(collection, index)?;

    let read_uncommitted = collection.read_uncommitted;
    let published = collection.published;
    let name = collection.name.as_str();
    match (collection.kind, collection.payload) {
        (StateKind::Value, StatePayload::Json) => {
            let _ = keyed.register(with_def(
                value_state::<JsonPassthroughStateCodec>(name),
                ttl_seconds,
                read_uncommitted,
                published,
            ));
        }
        (StateKind::Map, StatePayload::Json) => {
            let descriptor = with_def(
                map_state::<Utf8KeyCodec, JsonPassthroughStateCodec>(name),
                ttl_seconds,
                read_uncommitted,
                published,
            );
            let _ = keyed.register(with_keyset(descriptor, keyset_limit));
        }
        (StateKind::Deque, StatePayload::Json) => {
            let descriptor = with_def(
                deque_state::<JsonPassthroughStateCodec>(name),
                ttl_seconds,
                read_uncommitted,
                published,
            );
            let _ = keyed.register(with_capacity(descriptor, capacity));
        }
        (StateKind::Value, StatePayload::Message) => {
            let _ = keyed.register(with_def(
                message_state::<KafkaLoader<JsonBinaryCodec>>(name),
                ttl_seconds,
                read_uncommitted,
                published,
            ));
        }
        (StateKind::Map, StatePayload::Message) => {
            let descriptor = with_def(
                message_map_state::<Utf8KeyCodec, KafkaLoader<JsonBinaryCodec>>(name),
                ttl_seconds,
                read_uncommitted,
                published,
            );
            let _ = keyed.register(with_keyset(descriptor, keyset_limit));
        }
        (StateKind::Deque, StatePayload::Message) => {
            let descriptor = with_def(
                message_deque_state::<KafkaLoader<JsonBinaryCodec>>(name),
                ttl_seconds,
                read_uncommitted,
                published,
            );
            let _ = keyed.register(with_capacity(descriptor, capacity));
        }
    }

    Ok(())
}

fn collection_bounds(
    collection: &StateCollectionConfig,
    index: usize,
) -> Result<(Option<u32>, Option<NonZeroUsize>), FfiError> {
    if collection.published && collection.payload == StatePayload::Message {
        return Err(permanent_config(format!(
            "stateCollections[{index}].published: message collections cannot be published"
        )));
    }
    let keyset_limit = match collection.keyset_limit {
        Some(limit) => {
            if collection.kind != StateKind::Map {
                return Err(permanent_config(format!(
                    "stateCollections[{index}].keysetLimit: only valid for map collections"
                )));
            }
            if limit > MAX_KEYSET_LIMIT {
                return Err(permanent_config(format!(
                    "stateCollections[{index}].keysetLimit: must be between 0 and \
                     {MAX_KEYSET_LIMIT}"
                )));
            }
            Some(limit)
        }
        None => None,
    };

    let capacity = match collection.capacity {
        Some(cap) => {
            if collection.kind != StateKind::Deque {
                return Err(permanent_config(format!(
                    "stateCollections[{index}].capacity: only valid for deque collections"
                )));
            }
            Some(NonZeroUsize::new(cap as usize).ok_or_else(|| {
                permanent_config(format!(
                    "stateCollections[{index}].capacity: must be a positive integer"
                ))
            })?)
        }
        None => None,
    };

    Ok((keyset_limit, capacity))
}

/// Builds the keyed-state configuration from client options.
///
/// Registers each declared collection synchronously (before subscribe, hence
/// resubscribe-safe), rejecting duplicate names. Field-level validation names
/// the offending field; core validates the remaining rules (TTL ceiling, TTL
/// exceeding the recovery delay, identity conflicts) at consumer build.
///
/// # Errors
///
/// Returns [`FfiError::PermanentState`] if a keyed-state field is invalid, a
/// collection name is duplicated, or the cache directory is an empty string.
pub fn build_keyed_state_config(
    options: &ClientOptions,
) -> Result<KeyedStateConfiguration, FfiError> {
    let mut builder = KeyedStateConfiguration::builder();

    if let Some(dir) = &options.state_cache_dir {
        builder.cache_dir(PathBuf::from(dir));
    }

    if let Some(delay) = options.state_recovery_delay {
        let seconds = whole_seconds(delay, "stateRecoveryDelay", 1)?;
        builder.recovery_delay(CompactDuration::new(seconds));
    }

    if let Some(bytes) = options.state_cache_size_bytes {
        let bytes = u64::try_from(bytes).map_err(|_| {
            permanent_config("stateCacheSizeBytes must be greater than 0".to_owned())
        })?;
        let bytes = NonZeroU64::new(bytes).ok_or_else(|| {
            permanent_config("stateCacheSizeBytes must be greater than 0".to_owned())
        })?;
        builder.cache_size_bytes(Some(bytes));
    }

    if let Some(bytes) = options.state_read_cache_size_bytes {
        let bytes = u64::try_from(bytes).map_err(|_| {
            permanent_config("stateReadCacheSizeBytes must be greater than 0".to_owned())
        })?;
        let bytes = NonZeroU64::new(bytes).ok_or_else(|| {
            permanent_config("stateReadCacheSizeBytes must be greater than 0".to_owned())
        })?;
        builder.read_cache_size_bytes(Some(bytes));
    }

    match (
        options.state_read_cache_ttl,
        options.state_read_cache_disabled == Some(true),
    ) {
        (Some(_), true) => {
            return Err(permanent_config(
                "stateReadCacheTtl and stateReadCacheDisabled cannot both be set".to_owned(),
            ));
        }
        (Some(ttl), false) if ttl.is_zero() => {
            return Err(permanent_config(
                "stateReadCacheTtl must be greater than 0".to_owned(),
            ));
        }
        (Some(ttl), false) => {
            builder.read_cache_ttl(Some(ttl));
        }
        (None, true) => {
            builder.read_cache_ttl(None);
        }
        (None, false) => {}
    }

    if let Some(subsystem) = &options.state_subsystem {
        builder.subsystem(Some(
            SubsystemName::try_new(subsystem.clone())
                .map_err(|error| permanent_config(error.to_string()))?,
        ));
    }

    let mut keyed = builder
        .build()
        .map_err(|error| permanent_config(error.to_string()))?;

    if let Some(collections) = &options.state_collections {
        let mut seen = HashSet::with_capacity(collections.len());
        for (index, collection) in collections.iter().enumerate() {
            if !seen.insert(collection.name.as_str()) {
                return Err(permanent_config(format!(
                    "stateCollections[{index}].name: duplicate collection name {:?}",
                    collection.name
                )));
            }
            register_state_collection(&mut keyed, index, collection)?;
        }
    }

    Ok(keyed)
}

/// Creates all consumer-related configuration builders from client options.
///
/// Aggregates the individual builder functions into a single
/// [`ConsumerBuilders`] struct, which is the format expected by
/// [`prosody::high_level::HighLevelClient::new`].
///
/// # Errors
///
/// Returns an [`FfiError`] if any eagerly-finalized configuration fails
/// validation: the Kafka loader tuning ([`FfiError::LoaderConfig`]), the
/// deduplication cache capacity ([`FfiError::Validation`]), the telemetry
/// emitter configuration ([`FfiError::TelemetryConfig`], e.g. when an
/// environment variable such as `PROSODY_TELEMETRY_ENABLED` is invalid), or the
/// keyed-state registration ([`FfiError::PermanentState`]).
pub fn build_consumer_builders(options: &ClientOptions) -> Result<ConsumerBuilders, FfiError> {
    Ok(ConsumerBuilders {
        consumer: build_consumer_config(options)?,
        retry: build_retry_config(options),
        failure_topic: build_failure_topic_config(options),
        scheduler: build_scheduler_config(options),
        monopolization: build_monopolization_config(options),
        defer: build_defer_config(options),
        timeout: build_timeout_config(options),
        dedup: build_dedup_config(options)?,
        keyed_state: build_keyed_state_config(options)?,
        emitter: build_telemetry_emitter_config(options).build()?,
    })
}

/// Creates a Cassandra configuration builder from client options.
///
/// Configures the Cassandra connection for storing idempotence records,
/// including cluster nodes, keyspace, authentication, and data retention.
#[must_use]
pub fn build_cassandra_config(options: &ClientOptions) -> CassandraConfigurationBuilder {
    let mut builder = CassandraConfigurationBuilder::default();

    if let Some(nodes) = &options.cassandra_nodes {
        builder.nodes(nodes.clone());
    }

    if let Some(keyspace) = &options.cassandra_keyspace {
        builder.keyspace(keyspace);
    }

    if let Some(datacenter) = &options.cassandra_datacenter {
        builder.datacenter(Some(datacenter.clone()));
    }

    if let Some(rack) = &options.cassandra_rack {
        builder.rack(Some(rack.clone()));
    }

    if let Some(user) = &options.cassandra_user {
        builder.user(Some(user.clone()));
    }

    if let Some(password) = &options.cassandra_password {
        builder.password(Some(password.clone()));
    }

    if let Some(retention) = options.cassandra_retention {
        builder.retention(retention);
    }

    builder
}

/// Converts the client mode option to prosody's internal mode type.
///
/// Defaults to [`Mode::Pipeline`] when no mode is specified, which provides
/// balanced throughput and latency characteristics for most workloads.
#[must_use]
pub fn get_mode(options: &ClientOptions) -> Mode {
    match options.mode {
        Some(ClientMode::LowLatency) => Mode::LowLatency,
        Some(ClientMode::BestEffort) => Mode::BestEffort,
        Some(ClientMode::Pipeline) | None => Mode::Pipeline,
    }
}
