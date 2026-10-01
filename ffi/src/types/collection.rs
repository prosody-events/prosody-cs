//! The declaration of a keyed-state collection and the read cache policy of
//! a published collection.

use std::time::Duration;

use prosody::high_level::erased::ErasedReadCache;

/// The kind of a keyed-state collection and the options of that kind.
#[derive(Debug, Clone, Copy, PartialEq, Eq, uniffi::Enum)]
pub enum StateKind {
    /// A single-value collection.
    Value {
        /// The item payload.
        payload: StatePayload,
    },
    /// A `String`-keyed ordered map.
    Map {
        /// The item payload.
        payload: StatePayload,
        /// The optional ordered-scan keyset bound.
        keyset_limit: Option<u32>,
    },
    /// A deque.
    Deque {
        /// The item payload.
        payload: StatePayload,
        /// The optional window bound. Prosody applies it on push.
        capacity: Option<u32>,
    },
    /// A presence-only ordered set of `String` members.
    Set {
        /// The optional ordered-scan keyset bound.
        keyset_limit: Option<u32>,
    },
}

/// The item payload of a keyed-state collection.
#[derive(Debug, Clone, Copy, PartialEq, Eq, uniffi::Enum)]
pub enum StatePayload {
    /// JSON documents crossing as raw bytes.
    Json,
    /// The full Kafka message the handler received.
    Message,
}

/// The read cache policy of a published collection.
#[derive(Debug, Clone, Copy, PartialEq, Eq, uniffi::Enum)]
pub enum ReadCache {
    /// Bypasses the read cache.
    Disabled,
    /// Caches each read for `ttl`.
    Ttl {
        /// The cache duration.
        ttl: Duration,
    },
}

/// Declares one keyed-state collection to register before subscribe.
#[derive(Debug, Clone, uniffi::Record)]
pub struct StateCollectionConfig {
    /// The collection name.
    pub name: String,

    /// The collection kind and its options.
    pub kind: StateKind,

    /// The optional per-write TTL in whole seconds.
    #[uniffi(default = None)]
    pub ttl: Option<Duration>,

    /// Opts out of transactional staging when true.
    #[uniffi(default = false)]
    pub read_uncommitted: bool,

    /// Whether owners advertise this collection for cross-group reads.
    #[uniffi(default = false)]
    pub published: bool,
}

impl From<ReadCache> for ErasedReadCache {
    fn from(cache: ReadCache) -> Self {
        match cache {
            ReadCache::Disabled => Self::Disabled,
            ReadCache::Ttl { ttl } => Self::Ttl(ttl),
        }
    }
}
