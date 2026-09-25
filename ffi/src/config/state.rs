//! The keyed-state configuration and the registration of each declared
//! collection.
//!
//! Every error here is [`FfiError::PermanentState`]. A configuration mistake
//! cannot succeed on retry.

use prosody::ByteSize;
use prosody::codec::{JsonBinaryCodec, JsonBinaryMessageCodec};
use prosody::consumer::KeyedStateConfiguration;
use prosody::consumer::kafka_state::{message_deque_state, message_map_state, message_state};
use prosody::loader::KafkaLoader;
use prosody::state::descriptor::{
    DequeDescriptor, MapDescriptor, SetDescriptor, StateDescriptor, deque_state, map_state,
    set_state, value_state,
};
use prosody::state::order_codec::Utf8KeyCodec;
use prosody::subsystem::SubsystemName;
use prosody::timers::duration::CompactDuration;
use std::num::NonZeroUsize;
use std::path::PathBuf;
use std::time::Duration;

use crate::error::FfiError;
use crate::types::{ClientOptions, StateCollectionConfig, StateKind, StatePayload};

/// Builds the keyed-state configuration from client options.
///
/// Maps each declared collection into a typed descriptor. The normal Prosody
/// construction path validates the result.
///
/// # Errors
///
/// Returns [`FfiError::PermanentState`] if a host value cannot be mapped.
pub(super) fn build_keyed_state_config(
    options: &ClientOptions,
) -> Result<KeyedStateConfiguration, FfiError> {
    let mut builder = KeyedStateConfiguration::builder();

    if let Some(dir) = &options.state_cache_dir {
        builder.cache_dir(PathBuf::from(dir));
    }

    if let Some(size) = &options.state_owned_cache_size {
        let size = size
            .parse::<ByteSize>()
            .map_err(|error| FfiError::PermanentState(format!("stateOwnedCacheSize: {error}")))?;
        builder.owned_cache_size(Some(size));
    }

    if let Some(size) = &options.state_read_cache_size {
        let size = size
            .parse::<ByteSize>()
            .map_err(|error| FfiError::PermanentState(format!("stateReadCacheSize: {error}")))?;
        builder.read_cache_size(Some(size));
    }

    match (
        options.state_read_cache_ttl,
        options.state_read_cache_disabled == Some(true),
    ) {
        (Some(_), true) => {
            return Err(FfiError::PermanentState(
                "stateReadCacheTtl and stateReadCacheDisabled cannot both be set".to_owned(),
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

    if let Some(subsystem) = &options.subsystem {
        builder.subsystem(Some(
            SubsystemName::try_new(subsystem.clone())
                .map_err(|error| FfiError::PermanentState(error.to_string()))?,
        ));
    }

    let mut keyed = builder
        .build()
        .map_err(|error| FfiError::PermanentState(error.to_string()))?;

    if let Some(collections) = &options.state_collections {
        for (index, collection) in collections.iter().enumerate() {
            register_state_collection(&mut keyed, index, collection)?;
        }
    }

    Ok(keyed)
}

/// Validates one collection and registers its descriptor.
///
/// JSON collections use the [`BinaryPayload`](prosody::codec::BinaryPayload)
/// passthrough codec, so Rust never parses the JSON bytes. They claim the
/// shared `"json"` format id. Message collections use
/// `KafkaLoader<JsonBinaryMessageCodec>`, the codec of the consumer. Their
/// stored identity does not depend on the loader, because the message-ref
/// codec and resolver carry fixed `"message-ref"` identifiers. Thus this
/// registration matches the identity that the erased vend path checks.
///
/// # Errors
///
/// Returns [`FfiError::PermanentState`] if a host value cannot be mapped into
/// its Prosody type.
fn register_state_collection(
    keyed: &mut KeyedStateConfiguration,
    index: usize,
    collection: &StateCollectionConfig,
) -> Result<(), FfiError> {
    let ttl = collection
        .ttl
        .map(|ttl| whole_seconds(ttl, &format!("stateCollections[{index}].ttl")))
        .transpose()?;
    let capacity = checked_capacity(collection, index)?;
    let keyset_limit = collection.keyset_limit;
    let name = collection.name.as_str();

    match (collection.kind, collection.payload) {
        (StateKind::Value, StatePayload::Json) => {
            let descriptor = value_state::<JsonBinaryCodec>(name);
            let _ = keyed.register(with_def(descriptor, ttl, collection));
        }
        (StateKind::Map, StatePayload::Json) => {
            let descriptor = map_state::<Utf8KeyCodec, JsonBinaryCodec>(name);
            let descriptor = with_def(descriptor, ttl, collection);
            let _ = keyed.register(with_keyset(descriptor, keyset_limit));
        }
        (StateKind::Deque, StatePayload::Json) => {
            let descriptor = with_def(deque_state::<JsonBinaryCodec>(name), ttl, collection);
            let _ = keyed.register(with_capacity(descriptor, capacity));
        }
        (StateKind::Set, StatePayload::Json) => {
            let descriptor = with_def(set_state::<Utf8KeyCodec>(name), ttl, collection);
            let _ = keyed.register(with_set_keyset(descriptor, keyset_limit));
        }
        (StateKind::Value, StatePayload::Message) => {
            let descriptor = message_state::<KafkaLoader<JsonBinaryMessageCodec>>(name);
            let _ = keyed.register(with_def(descriptor, ttl, collection));
        }
        (StateKind::Map, StatePayload::Message) => {
            let descriptor =
                message_map_state::<Utf8KeyCodec, KafkaLoader<JsonBinaryMessageCodec>>(name);
            let descriptor = with_def(descriptor, ttl, collection);
            let _ = keyed.register(with_keyset(descriptor, keyset_limit));
        }
        (StateKind::Deque, StatePayload::Message) => {
            let descriptor = message_deque_state::<KafkaLoader<JsonBinaryMessageCodec>>(name);
            let descriptor = with_def(descriptor, ttl, collection);
            let _ = keyed.register(with_capacity(descriptor, capacity));
        }
        (StateKind::Set, StatePayload::Message) => {
            return Err(FfiError::PermanentState(format!(
                "stateCollections[{index}].payload: a set stores no message payload"
            )));
        }
    }

    Ok(())
}

/// Checks that each optional bound suits the collection kind.
///
/// A keyset limit is valid only on a map or a set. A capacity is valid only on
/// a deque and must be positive.
///
/// # Errors
///
/// Returns [`FfiError::PermanentState`] if a bound does not suit the kind.
fn checked_capacity(
    collection: &StateCollectionConfig,
    index: usize,
) -> Result<Option<NonZeroUsize>, FfiError> {
    if collection.keyset_limit.is_some()
        && !matches!(collection.kind, StateKind::Map | StateKind::Set)
    {
        return Err(FfiError::PermanentState(format!(
            "stateCollections[{index}].keysetLimit: only valid for map and set collections"
        )));
    }

    let Some(capacity) = collection.capacity else {
        return Ok(None);
    };
    if collection.kind != StateKind::Deque {
        return Err(FfiError::PermanentState(format!(
            "stateCollections[{index}].capacity: only valid for deque collections"
        )));
    }
    NonZeroUsize::new(capacity as usize)
        .map(Some)
        .ok_or_else(|| {
            FfiError::PermanentState(format!(
                "stateCollections[{index}].capacity: must be a positive integer"
            ))
        })
}

/// Converts a duration into the whole seconds that Prosody descriptors use.
///
/// The field arrives as a [`Duration`] (a C# `TimeSpan`), so a fractional or
/// out-of-range value reaches this check and is not truncated. Prosody keeps
/// the semantic duration limits.
///
/// # Errors
///
/// Returns [`FfiError::PermanentState`] if the duration cannot be represented
/// as whole `u32` seconds.
fn whole_seconds(duration: Duration, field: &str) -> Result<u32, FfiError> {
    if duration.subsec_nanos() != 0 {
        return Err(FfiError::PermanentState(format!(
            "{field}: must be a whole number of seconds"
        )));
    }
    u32::try_from(duration.as_secs())
        .map_err(|_| FfiError::PermanentState(format!("{field}: exceeds the u32 seconds range")))
}

/// Applies the shared descriptor options: TTL, commit mode, and publication.
fn with_def<D: StateDescriptor>(
    mut descriptor: D,
    ttl_seconds: Option<u32>,
    collection: &StateCollectionConfig,
) -> D {
    if let Some(ttl) = ttl_seconds {
        descriptor = descriptor.ttl(CompactDuration::new(ttl));
    }
    if collection.read_uncommitted == Some(true) {
        descriptor = descriptor.read_uncommitted();
    }
    descriptor.published(collection.published)
}

/// Applies the map keyset bound when configured.
fn with_keyset<KC, V>(
    descriptor: MapDescriptor<KC, V>,
    keyset_limit: Option<u32>,
) -> MapDescriptor<KC, V> {
    match keyset_limit {
        Some(limit) => descriptor.keyset_limit(limit as usize),
        None => descriptor,
    }
}

/// Applies the set keyset bound when configured.
fn with_set_keyset<KC>(
    descriptor: SetDescriptor<KC>,
    keyset_limit: Option<u32>,
) -> SetDescriptor<KC> {
    match keyset_limit {
        Some(limit) => descriptor.keyset_limit(limit as usize),
        None => descriptor,
    }
}

/// Applies the deque capacity bound when configured.
fn with_capacity<T>(
    descriptor: DequeDescriptor<T>,
    capacity: Option<NonZeroUsize>,
) -> DequeDescriptor<T> {
    match capacity {
        Some(capacity) => descriptor.capacity(capacity),
        None => descriptor,
    }
}
