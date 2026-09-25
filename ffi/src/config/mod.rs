//! Converts [`ClientOptions`] into the prosody builders that construct a
//! [`prosody::high_level::HighLevelClient`].
//!
//! Each function sets a builder field only when its option is `Some`. A `None`
//! option keeps the builder default and its environment variable fallback.
//! Most builders validate when Prosody finalizes them. A function that
//! finalizes a nested configuration returns a [`Result`].
//!
//! - This module: the producer, consumer, peer, telemetry, and Cassandra
//!   builders, and the client mode.
//! - `middleware`: the consumer middleware builders.
//! - `state`: the keyed-state configuration.

mod middleware;
mod state;

use prosody::PeerConfiguration;
use prosody::PeerEndpoint;
use prosody::cassandra::config::CassandraConfigurationBuilder;
use prosody::consumer::ConsumerConfigurationBuilder;
use prosody::consumer::SpanRelation as ProsodySpanRelation;
use prosody::high_level::ConsumerBuilders;
use prosody::high_level::mode::Mode;
use prosody::loader::KafkaLoaderConfiguration;
use prosody::producer::ProducerConfigurationBuilder;
use prosody::telemetry::emitter::{
    TelemetryEmitterConfiguration, TelemetryEmitterConfigurationBuilder,
};
use std::net::SocketAddr;

use crate::error::FfiError;
use crate::types::{ClientMode, ClientOptions, SpanRelation};
use middleware::{
    build_dedup_config, build_defer_config, build_failure_topic_config,
    build_monopolization_config, build_retry_config, build_scheduler_config, build_timeout_config,
};
use state::build_keyed_state_config;

impl From<SpanRelation> for ProsodySpanRelation {
    fn from(relation: SpanRelation) -> Self {
        match relation {
            SpanRelation::Child => Self::Child,
            SpanRelation::FollowsFrom => Self::FollowsFrom,
        }
    }
}

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
/// Returns [`FfiError::LoaderConfig`] if the Kafka loader tuning
/// derived from `loader_cache_size`, `loader_seek_timeout`, or
/// `loader_discard_threshold` fails validation.
fn build_consumer_config(
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
        builder.probe_port((probe_port != 0).then_some(probe_port));
    }

    if let Some(slab_size) = options.slab_size {
        builder.slab_size(slab_size);
    }

    if let Some(message_spans) = options.message_spans {
        builder.message_spans(message_spans.into());
    }

    if let Some(timer_spans) = options.timer_spans {
        builder.timer_spans(timer_spans.into());
    }

    // The loader tuning is consumer-wide. Attach it only when the caller sets
    // a loader option, so the default keeps its environment fallbacks.
    if options.loader_cache_size.is_some()
        || options.loader_seek_timeout.is_some()
        || options.loader_discard_threshold.is_some()
    {
        let mut loader = KafkaLoaderConfiguration::builder();

        if let Some(cache_size) = options.loader_cache_size {
            loader.cache_size(cache_size as usize);
        }

        if let Some(seek_timeout) = options.loader_seek_timeout {
            loader.seek_timeout(seek_timeout);
        }

        if let Some(discard_threshold) = options.loader_discard_threshold {
            loader.discard_threshold(i64::from(discard_threshold));
        }

        builder.loader(loader.build()?);
    }

    Ok(builder)
}

/// Builds the peer configuration from client options.
///
/// # Errors
///
/// Returns [`FfiError::PermanentState`] if a peer option cannot be parsed or
/// the configuration fails validation.
fn build_peer_config(options: &ClientOptions) -> Result<PeerConfiguration, FfiError> {
    let mut builder = PeerConfiguration::builder();

    if let Some(value) = &options.peer_bind_address {
        builder.bind_address(
            value
                .parse::<SocketAddr>()
                .map_err(|error| FfiError::PermanentState(format!("peer_bind_address: {error}")))?,
        );
    }

    if let Some(value) = &options.peer_advertised_connect {
        builder.advertised_connect(PeerEndpoint::try_from(value.clone()).map_err(|error| {
            FfiError::PermanentState(format!("peer_advertised_connect: {error}"))
        })?);
    }

    if let Some(value) = &options.peer_network_name {
        builder.network_name(value.clone());
    }

    if let Some(value) = options.peer_cache_capacity {
        builder.peer_cache_capacity(
            usize::try_from(value).map_err(|error| {
                FfiError::PermanentState(format!("peer_cache_capacity: {error}"))
            })?,
        );
    }

    if let Some(value) = options.peer_registration_ttl {
        builder.registration_ttl(value);
    }

    builder
        .build()
        .map_err(|error| FfiError::PermanentState(error.to_string()))
}

/// Creates a telemetry emitter configuration builder from client options.
///
/// Configures the background Kafka emitter that publishes message and timer
/// lifecycle events to a dedicated telemetry topic.
fn build_telemetry_emitter_config(options: &ClientOptions) -> TelemetryEmitterConfigurationBuilder {
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
/// Returns an [`FfiError`] if any eagerly-finalized configuration fails
/// validation: the Kafka loader tuning ([`FfiError::LoaderConfig`]), the
/// deduplication cache capacity ([`FfiError::Validation`]), the telemetry
/// emitter configuration ([`FfiError::TelemetryConfig`], e.g. when an
/// environment variable such as `PROSODY_TELEMETRY_ENABLED` is invalid), or the
/// keyed-state or peer configuration ([`FfiError::PermanentState`]).
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
        peer: build_peer_config(options)?,
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
