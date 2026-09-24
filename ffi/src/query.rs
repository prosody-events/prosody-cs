//! Keyed-state query settings that cross the FFI boundary.
//!
//! Each record converts into the core query it names. The conversion sets the
//! direction first because core reads start and end edges in query order.

use std::num::NonZeroUsize;

use prosody::state::{DequeQuery, ErasedKeyQuery};

use crate::error::FfiError;
use crate::state::{ScanDirection, platform_index};

/// One start or end edge of a map or set query.
#[derive(Debug, Clone, PartialEq, Eq, uniffi::Enum)]
pub enum KeyEdge {
    /// The query includes the key.
    Inclusive {
        /// The edge key.
        key: String,
    },
    /// The query excludes the key.
    Exclusive {
        /// The edge key.
        key: String,
    },
}

/// Settings for a map entries, map keys, or set members query.
///
/// `start` and `end` are in query order: a backward query starts at the high
/// end. Every setting narrows the selection.
#[derive(Debug, Clone, PartialEq, Eq, uniffi::Record)]
pub struct KeyQuery {
    /// The iteration order.
    pub direction: ScanDirection,
    /// Keeps keys that start with this prefix.
    pub prefix: Option<String>,
    /// The first key in query order.
    pub start: Option<KeyEdge>,
    /// The last key in query order.
    pub end: Option<KeyEdge>,
    /// The maximum number of returned items. Must be positive.
    pub limit: Option<u32>,
}

/// One start or end edge of a deque query.
#[derive(Debug, Clone, Copy, PartialEq, Eq, uniffi::Enum)]
pub enum PositionEdge {
    /// The query includes the position.
    Inclusive {
        /// The front-relative position.
        position: u64,
    },
    /// The query excludes the position.
    Exclusive {
        /// The front-relative position.
        position: u64,
    },
}

/// An ascending half-open range of front-relative deque positions.
#[derive(Debug, Clone, Copy, PartialEq, Eq, uniffi::Record)]
pub struct PositionRange {
    /// The first position in the range.
    pub start: u64,
    /// The position after the range, or `None` for no upper bound.
    pub end: Option<u64>,
}

/// Settings for a deque values query.
///
/// `start` and `end` are in query order. `range` is ascending and applies in
/// either direction. Every setting narrows the selection.
#[derive(Debug, Clone, Copy, PartialEq, Eq, uniffi::Record)]
pub struct PositionQuery {
    /// The iteration order.
    pub direction: ScanDirection,
    /// The first position in query order.
    pub start: Option<PositionEdge>,
    /// The last position in query order.
    pub end: Option<PositionEdge>,
    /// Keeps positions within this ascending range.
    pub range: Option<PositionRange>,
    /// The maximum number of returned items. Must be positive.
    pub limit: Option<u32>,
}

impl TryFrom<KeyQuery> for ErasedKeyQuery {
    type Error = FfiError;

    fn try_from(query: KeyQuery) -> Result<Self, FfiError> {
        let mut core = ErasedKeyQuery::new().direction(query.direction.into());
        if let Some(prefix) = query.prefix {
            core = core.prefix(prefix);
        }
        core = match query.start {
            Some(KeyEdge::Inclusive { key }) => core.from(key),
            Some(KeyEdge::Exclusive { key }) => core.after(key),
            None => core,
        };
        core = match query.end {
            Some(KeyEdge::Inclusive { key }) => core.to(key),
            Some(KeyEdge::Exclusive { key }) => core.before(key),
            None => core,
        };
        Ok(match query.limit {
            Some(limit) => core.limit(positive(limit)?),
            None => core,
        })
    }
}

impl TryFrom<PositionQuery> for DequeQuery {
    type Error = FfiError;

    fn try_from(query: PositionQuery) -> Result<Self, FfiError> {
        let mut core = DequeQuery::new().direction(query.direction.into());
        core = match query.start {
            Some(PositionEdge::Inclusive { position }) => core.from(platform_index(position)?),
            Some(PositionEdge::Exclusive { position }) => core.after(platform_index(position)?),
            None => core,
        };
        core = match query.end {
            Some(PositionEdge::Inclusive { position }) => core.to(platform_index(position)?),
            Some(PositionEdge::Exclusive { position }) => core.before(platform_index(position)?),
            None => core,
        };
        core = match query.range {
            Some(PositionRange {
                start,
                end: Some(end),
            }) => core.range(platform_index(start)?..platform_index(end)?),
            Some(PositionRange { start, end: None }) => core.range(platform_index(start)?..),
            None => core,
        };
        Ok(match query.limit {
            Some(limit) => core.limit(positive(limit)?),
            None => core,
        })
    }
}

/// Converts a query limit. The C# wrapper rejects zero before the call.
fn positive(limit: u32) -> Result<NonZeroUsize, FfiError> {
    NonZeroUsize::new(limit as usize)
        .ok_or_else(|| FfiError::TransientState("query limit must be positive".to_owned()))
}
