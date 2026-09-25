//! Builders for the consumer middleware stack: retry, failure topic,
//! scheduler, monopolization, defer, timeout, and deduplication.

use prosody::consumer::middleware::deduplication::DeduplicationConfigurationBuilder;
use prosody::consumer::middleware::defer::DeferConfigurationBuilder;
use prosody::consumer::middleware::monopolization::MonopolizationConfigurationBuilder;
use prosody::consumer::middleware::retry::RetryConfigurationBuilder;
use prosody::consumer::middleware::scheduler::SchedulerConfigurationBuilder;
use prosody::consumer::middleware::timeout::TimeoutConfigurationBuilder;
use prosody::consumer::middleware::topic::FailureTopicConfigurationBuilder;
use std::num::NonZeroUsize;
use validator::{ValidationError, ValidationErrors};

use crate::error::FfiError;
use crate::types::ClientOptions;

/// Creates a retry configuration builder from client options.
///
/// Configures exponential backoff retry behavior with base delay, maximum
/// retry count, and maximum delay cap.
#[must_use]
pub(super) fn build_retry_config(options: &ClientOptions) -> RetryConfigurationBuilder {
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
pub(super) fn build_failure_topic_config(
    options: &ClientOptions,
) -> FailureTopicConfigurationBuilder {
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
pub(super) fn build_scheduler_config(options: &ClientOptions) -> SchedulerConfigurationBuilder {
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
pub(super) fn build_monopolization_config(
    options: &ClientOptions,
) -> MonopolizationConfigurationBuilder {
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
pub(super) fn build_defer_config(options: &ClientOptions) -> DeferConfigurationBuilder {
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
pub(super) fn build_timeout_config(options: &ClientOptions) -> TimeoutConfigurationBuilder {
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
/// Returns [`FfiError::Validation`] if `idempotence_cache_size` is zero. The
/// core API models the cache capacity as a non-zero value.
pub(super) fn build_dedup_config(
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
