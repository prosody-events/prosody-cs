//! The declaration of a keyed-state collection: its kind, its payload, and
//! its options.

use std::time::Duration;

/// The kind of a keyed-state collection.
#[derive(Debug, Clone, Copy, PartialEq, Eq, uniffi::Enum)]
pub enum StateKind {
    /// A single-value collection.
    Value,
    /// A `String`-keyed ordered map.
    Map,
    /// A deque.
    Deque,
    /// A presence-only ordered set of `String` members. Its payload must be
    /// [`StatePayload::Json`] because a set stores no items.
    Set,
}

/// The item payload of a keyed-state collection.
#[derive(Debug, Clone, Copy, PartialEq, Eq, uniffi::Enum)]
pub enum StatePayload {
    /// JSON documents crossing as raw bytes.
    Json,
    /// The full Kafka message the handler received.
    Message,
}

/// Declares one keyed-state collection to register before subscribe.
#[derive(Debug, Clone, uniffi::Record)]
pub struct StateCollectionConfig {
    /// The collection name. Must be non-empty and unique within the client's
    /// definition set.
    pub name: String,

    /// The collection kind.
    pub kind: StateKind,

    /// The item payload.
    pub payload: StatePayload,

    /// Optional per-write TTL. Must be a whole number of seconds of at least 1
    /// (fractional and sub-second values are rejected). The Cassandra TTL limit
    /// applies.
    #[uniffi(default = None)]
    pub ttl: Option<Duration>,

    /// Optional opt-out of transactional staging (read-uncommitted, at-least
    /// once). Defaults to transactional.
    #[uniffi(default = None)]
    pub read_uncommitted: Option<bool>,

    /// Optional map or set keyset bound (`0..=4096`; default 128 core-side;
    /// `0` disables ordered-scan tracking). Invalid on value or deque
    /// collections.
    #[uniffi(default = None)]
    pub keyset_limit: Option<u32>,

    /// Optional deque-only capacity bound (positive). Runtime-only — never
    /// persisted, not part of identity; enforced lazily on push. Invalid on
    /// value, map, or set collections.
    #[uniffi(default = None)]
    pub capacity: Option<u32>,

    /// Whether owners advertise this collection for cross-group reads.
    #[uniffi(default = false)]
    pub published: bool,

    /// Per-reader cache TTL override.
    #[uniffi(default = None)]
    pub read_cache_ttl: Option<Duration>,

    /// Whether readers bypass their cache for this collection.
    #[uniffi(default = false)]
    pub read_cache_disabled: bool,
}
