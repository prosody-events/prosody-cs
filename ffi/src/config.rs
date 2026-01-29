//! Configuration conversion utilities.
//!
//! This module provides functions to convert [`ClientOptions`] into the various
//! prosody builder types needed to construct a [`HighLevelClient`].
//!
//! The pattern follows sibling wrappers (prosody-js, prosody-py, prosody-rb):
//! - Only set builder fields when the option is `Some`
//! - Let builder defaults handle environment variable fallbacks

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

use crate::types::{ClientMode, ClientOptions};

/// Builds a [`ProducerConfigurationBuilder`] from [`ClientOptions`].
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

/// Builds a [`ConsumerConfigurationBuilder`] from [`ClientOptions`].
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

    if let Some(max_enqueued_per_key) = options.max_enqueued_per_key {
        builder.max_enqueued_per_key(max_enqueued_per_key as usize);
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

    // Handle probe port: None = use default, 0 = disabled, 1-65535 = use that port
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

/// Builds a [`RetryConfigurationBuilder`] from [`ClientOptions`].
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

/// Builds a [`FailureTopicConfigurationBuilder`] from [`ClientOptions`].
#[must_use]
pub fn build_failure_topic_config(options: &ClientOptions) -> FailureTopicConfigurationBuilder {
    let mut builder = FailureTopicConfigurationBuilder::default();

    if let Some(topic) = &options.failure_topic {
        builder.failure_topic(topic);
    }

    builder
}

/// Builds a [`SchedulerConfigurationBuilder`] from [`ClientOptions`].
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

/// Builds a [`MonopolizationConfigurationBuilder`] from [`ClientOptions`].
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

/// Builds a [`DeferConfigurationBuilder`] from [`ClientOptions`].
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

/// Builds a [`TimeoutConfigurationBuilder`] from [`ClientOptions`].
#[must_use]
pub fn build_timeout_config(options: &ClientOptions) -> TimeoutConfigurationBuilder {
    let mut builder = TimeoutConfigurationBuilder::default();

    if let Some(timeout) = options.timeout {
        builder.timeout(Some(timeout));
    }

    builder
}

/// Builds [`ConsumerBuilders`] from [`ClientOptions`].
///
/// This creates all the consumer-related configuration builders in one call,
/// which is what `HighLevelClient::new()` expects.
#[must_use]
pub fn build_consumer_builders(options: &ClientOptions) -> ConsumerBuilders {
    ConsumerBuilders {
        consumer: build_consumer_config(options),
        retry: build_retry_config(options),
        failure_topic: build_failure_topic_config(options),
        scheduler: build_scheduler_config(options),
        monopolization: build_monopolization_config(options),
        defer: build_defer_config(options),
        timeout: build_timeout_config(options),
    }
}

/// Builds a [`CassandraConfigurationBuilder`] from [`ClientOptions`].
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

/// Converts [`ClientMode`] to prosody's [`Mode`].
#[must_use]
pub fn get_mode(options: &ClientOptions) -> Mode {
    match options.mode {
        Some(ClientMode::LowLatency) => Mode::LowLatency,
        Some(ClientMode::BestEffort) => Mode::BestEffort,
        Some(ClientMode::Pipeline) | None => Mode::Pipeline,
    }
}
