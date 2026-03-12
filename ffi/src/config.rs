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
//! - All functions are infallible; validation happens when builders are
//!   finalized

use prosody::cassandra::config::CassandraConfigurationBuilder;
use prosody::consumer::ConsumerConfigurationBuilder;
use prosody::consumer::middleware::defer::DeferConfigurationBuilder;
use prosody::consumer::middleware::monopolization::MonopolizationConfigurationBuilder;
use prosody::consumer::middleware::retry::RetryConfigurationBuilder;
use prosody::consumer::middleware::scheduler::SchedulerConfigurationBuilder;
use prosody::consumer::middleware::timeout::TimeoutConfigurationBuilder;
use prosody::consumer::middleware::topic::FailureTopicConfigurationBuilder;
use prosody::high_level::ConsumerBuilders;
use prosody::high_level::mode::Mode;
use prosody::producer::ProducerConfigurationBuilder;
use prosody::telemetry::emitter::{
    TelemetryEmitterConfiguration, TelemetryEmitterConfigurationBuilder,
    TelemetryEmitterConfigurationBuilderError,
};

use crate::types::{ClientMode, ClientOptions};

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
/// topic subscriptions, idempotence, and flow control parameters.
///
/// # Probe Port Handling
///
/// The `probe_port` option uses special semantics:
/// - `None`: Use builder default (typically enabled with auto-assigned port)
/// - `Some(0)`: Explicitly disable the probe endpoint
/// - `Some(1..=65535)`: Use the specified port number
#[must_use]
pub fn build_consumer_config(options: &ClientOptions) -> ConsumerConfigurationBuilder {
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

    if let Some(cache_size) = options.idempotence_cache_size {
        builder.idempotence_cache_size(cache_size as usize);
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

    builder
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

    if let Some(cache_size) = options.defer_cache_size {
        builder.cache_size(cache_size as usize);
    }

    if let Some(seek_timeout) = options.defer_seek_timeout {
        builder.seek_timeout(seek_timeout);
    }

    if let Some(discard_threshold) = options.defer_discard_threshold {
        builder.discard_threshold(i64::from(discard_threshold));
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

/// Creates all consumer-related configuration builders from client options.
///
/// Aggregates the individual builder functions into a single
/// [`ConsumerBuilders`] struct, which is the format expected by
/// [`prosody::high_level::HighLevelClient::new`].
///
/// # Errors
///
/// Returns [`TelemetryEmitterConfigurationBuilderError`] if the telemetry
/// emitter configuration cannot be built, which occurs when an environment
/// variable (e.g. `PROSODY_TELEMETRY_ENABLED`) contains an invalid value.
pub fn build_consumer_builders(
    options: &ClientOptions,
) -> Result<ConsumerBuilders, TelemetryEmitterConfigurationBuilderError> {
    Ok(ConsumerBuilders {
        consumer: build_consumer_config(options),
        retry: build_retry_config(options),
        failure_topic: build_failure_topic_config(options),
        scheduler: build_scheduler_config(options),
        monopolization: build_monopolization_config(options),
        defer: build_defer_config(options),
        timeout: build_timeout_config(options),
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
