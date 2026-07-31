//! Erased native layer for keyed state.
//!
//! Wraps the boxed erased handles from [`prosody::consumer::event_context`] as
//! `UniFFI` objects. Collections are addressed by name; JSON payloads cross as
//! raw bytes ([`BinaryPayload`], the passthrough codec — Rust never parses the
//! JSON, exactly like the message-payload path) and Kafka-message items cross
//! as the same [`Message`] object handlers already receive.
//!
//! Every asynchronous operation extracts the C# carrier and activates it while
//! polling the erased future, allowing core's semantic collection span to join
//! the event trace without an extra binding span. Scans activate the carrier
//! while core constructs its stream span; pulls transport vectors of up to 256
//! immediately-ready items without creating per-chunk binding spans.
//!
//! Errors carry their category structurally: the erased seam's
//! [`ErasedCategory`](prosody::consumer::event_context::ErasedCategory) folds
//! into the two flat [`FfiError`] state variants, which uniffi-bindgen-cs
//! renders as distinct exception subclasses the typed layer branches on by
//! type, never by parsing the human message. No fencing or cursor safety lives
//! here: those are core-owned and this layer only transports and types.
//! Caller-mistake conditions the glue detects (a `null` write, a wrong item
//! shape, an unrepresentable value) reject `TransientState` — a caller code
//! error retries and stays visible rather than discarding the message (see
//! CLAUDE.md error-classification).

use std::collections::HashMap;
use std::num::NonZeroUsize;
use std::sync::Arc;

use opentelemetry::Context;
use opentelemetry::propagation::{TextMapCompositePropagator, TextMapPropagator};
use opentelemetry::trace::FutureExt;

use prosody::codec::{BinaryPayload, ErasedStateCodec};
use prosody::consumer::event_context::{BoxDequeState, BoxMapState, BoxStateCursor, BoxValueState};
use prosody::consumer::message::ConsumerMessage;
use prosody::state::Direction;

use crate::error::FfiError;
use crate::message::Message;

/// Maximum number of immediately-ready scan items transported across the FFI
/// boundary in one vector. Core owns ready draining, error ordering, and pull
/// serialization; this binding owns only the transport cap and conversion.
const SCAN_READY_CHUNK_SIZE: NonZeroUsize = match NonZeroUsize::new(256) {
    Some(size) => size,
    None => NonZeroUsize::MIN,
};

/// The direction a scan walks the collection's key or index order.
#[derive(Clone, Copy, Debug, PartialEq, Eq, uniffi::Enum)]
pub enum ScanDirection {
    /// Ascending key or index order.
    Forward,
    /// Descending key or index order.
    Backward,
}

impl From<ScanDirection> for Direction {
    fn from(direction: ScanDirection) -> Self {
        match direction {
            ScanDirection::Forward => Direction::Forward,
            ScanDirection::Backward => Direction::Backward,
        }
    }
}

/// An item read from a state collection.
///
/// A handle's payload flavour fully determines which variant it yields, so a
/// read can never mismatch the collection it came from.
#[derive(uniffi::Enum)]
pub enum StateItem {
    /// Raw JSON document bytes, copied verbatim from the collection.
    Json {
        /// The stored JSON document bytes.
        bytes: Vec<u8>,
    },
    /// A Kafka message resolved from a message collection.
    ///
    /// Named `MessageItem` rather than `Message` to avoid a name collision with
    /// the generated C# `Message` object type inside the object-bearing enum
    /// variant, which the C# binding generator mis-qualifies.
    MessageItem {
        /// The resolved Kafka message.
        message: Arc<Message>,
    },
}

/// An entry yielded by a scan cursor, one variant per (collection, payload)
/// pair.
#[derive(uniffi::Enum)]
pub enum StateScanItem {
    /// A deque element whose payload is raw JSON bytes.
    DequeJson {
        /// The element's JSON document bytes.
        bytes: Vec<u8>,
    },
    /// A map entry whose value is raw JSON bytes.
    MapJson {
        /// The entry's key.
        key: String,
        /// The entry's JSON document bytes.
        bytes: Vec<u8>,
    },
    /// A deque element whose payload is a Kafka message.
    DequeMessage {
        /// The resolved Kafka message.
        message: Arc<Message>,
    },
    /// A map entry whose value is a Kafka message.
    MapMessage {
        /// The entry's key.
        key: String,
        /// The resolved Kafka message.
        message: Arc<Message>,
    },
    /// A map entry key decoded without reading or resolving its value.
    ///
    /// Named `MapKey` rather than `Key`: a uniffi variant `Key` carrying a
    /// field `key` generates a nested C# type `StateScanItem.Key` with a
    /// property `Key`, which is CS0542 (a member may not share its enclosing
    /// type's name). `MapKey` sidesteps the collision, exactly as
    /// [`StateItem::MessageItem`] avoids a bare `Message`.
    MapKey {
        /// The entry's key.
        key: String,
    },
}

/// A carrier map consumed by value while extracting its OpenTelemetry context.
///
/// Taking the map by value into this newtype (rather than borrowing a by-value
/// argument) lets the synchronous `scan` methods satisfy
/// `clippy::needless_pass_by_value` without an allow — [`Self::into_context`]
/// consumes `self`, and self-by-value is never flagged.
struct OwnedCarrier(HashMap<String, String>);

impl OwnedCarrier {
    /// Extracts the propagated OpenTelemetry context, consuming the carrier.
    fn into_context(self, propagator: &TextMapCompositePropagator) -> Context {
        propagator.extract(&self.0)
    }
}

/// The two payload flavours a value handle wraps: owned JSON bytes or
/// loader-resolved Kafka messages.
pub(crate) enum ValueStateVariant {
    /// A JSON value collection.
    Json(BoxValueState<BinaryPayload>),
    /// A Kafka-message value collection.
    Message(BoxValueState<ConsumerMessage<BinaryPayload>>),
}

/// The two payload flavours a map handle wraps.
pub(crate) enum MapStateVariant {
    /// A JSON map collection.
    Json(BoxMapState<BinaryPayload>),
    /// A Kafka-message map collection.
    Message(BoxMapState<ConsumerMessage<BinaryPayload>>),
}

/// The two payload flavours a deque handle wraps.
pub(crate) enum DequeStateVariant {
    /// A JSON deque collection.
    Json(BoxDequeState<BinaryPayload>),
    /// A Kafka-message deque collection.
    Message(BoxDequeState<ConsumerMessage<BinaryPayload>>),
}

/// The four cursor flavours a scan yields, one per (collection, payload) pair.
pub(crate) enum CursorVariant {
    /// A deque JSON scan yielding document bytes.
    DequeJson(BoxStateCursor<BinaryPayload>),
    /// A map JSON scan yielding `(key, bytes)` entries.
    MapJson(BoxStateCursor<(String, BinaryPayload)>),
    /// A deque message scan yielding messages.
    DequeMessage(BoxStateCursor<ConsumerMessage<BinaryPayload>>),
    /// A map message scan yielding `(key, message)` entries.
    MapMessage(BoxStateCursor<(String, ConsumerMessage<BinaryPayload>)>),
    /// A map key-only scan yielding decoded keys, payload-agnostic.
    MapKeys(BoxStateCursor<String>),
}

/// Builds a transient state error for a caller-caused condition the glue
/// detects (a `null` write or a wrong item shape). Caller mistakes are
/// `TransientState`, never permanent, so the offset is not committed and the
/// message is retried rather than silently lost (see CLAUDE.md).
fn transient_state(message: String) -> FfiError {
    FfiError::TransientState(message)
}

/// Rejects a JSON `null` document write before it crosses to core.
///
/// `null` is not a storable value — it is the erased seam's name for absence,
/// so core would reject it `Permanent`. The glue rejects it first as
/// `TransientState` (a caller mistake retries and stays visible); `advice`
/// names the collection's deletion verb.
fn reject_null(payload: &BinaryPayload, collection: &str, advice: &str) -> Result<(), FfiError> {
    if payload.is_absent_sentinel() {
        return Err(transient_state(format!(
            "collection {collection:?}: JSON null is not a storable value{advice}"
        )));
    }
    Ok(())
}

/// Wraps a JSON payload as the [`StateItem::Json`] transport variant.
fn json_item(payload: BinaryPayload) -> StateItem {
    StateItem::Json {
        bytes: payload.bytes,
    }
}

/// Wraps a resolved Kafka message as the [`StateItem::MessageItem`] transport
/// variant.
fn message_item(message: ConsumerMessage<BinaryPayload>) -> StateItem {
    StateItem::MessageItem {
        message: Arc::new(Message::new(message)),
    }
}

/// Erased single-value state handle, vended per event.
///
/// Wraps the boxed erased value handle plus the propagator used to open each
/// operation's span and the collection name (named in wrong-shape and
/// null-write errors). JSON values cross as raw bytes; message collections
/// cross as the [`Message`] object.
#[derive(uniffi::Object)]
pub struct ValueStateHandle {
    /// The registered collection name, named in caller-mistake errors.
    pub(crate) name: String,
    /// The wrapped erased value handle.
    pub(crate) state: ValueStateVariant,
    /// The propagator used to re-establish the event parent per operation.
    pub(crate) propagator: Arc<TextMapCompositePropagator>,
}

#[uniffi::export(async_runtime = "tokio")]
impl ValueStateHandle {
    /// Reads the current value.
    ///
    /// # Errors
    ///
    /// Returns a state error carrying the erased category if the read fails.
    pub async fn get(
        &self,
        carrier: HashMap<String, String>,
    ) -> Result<Option<StateItem>, FfiError> {
        let context = self.propagator.extract(&carrier);
        match &self.state {
            ValueStateVariant::Json(handle) => handle
                .get()
                .with_context(context)
                .await
                .map(|item| item.map(json_item))
                .map_err(FfiError::from),
            ValueStateVariant::Message(handle) => handle
                .get()
                .with_context(context)
                .await
                .map(|item| item.map(message_item))
                .map_err(FfiError::from),
        }
    }

    /// Buffers a write of a JSON document.
    ///
    /// # Errors
    ///
    /// Returns `TransientState` when the document is JSON `null` (use
    /// `ClearAsync` to remove the value) or when the collection is a
    /// Kafka-message collection; otherwise a state error carrying the erased
    /// category if the write fails.
    pub async fn set_json(
        &self,
        bytes: Vec<u8>,
        carrier: HashMap<String, String>,
    ) -> Result<(), FfiError> {
        let context = self.propagator.extract(&carrier);
        match &self.state {
            ValueStateVariant::Json(handle) => {
                let payload = BinaryPayload::new(bytes, None::<String>, None::<String>);
                reject_null(&payload, &self.name, "; use ClearAsync to remove the value")?;
                handle
                    .set(payload)
                    .with_context(context)
                    .await
                    .map_err(FfiError::from)
            }
            ValueStateVariant::Message(_) => Err(transient_state(format!(
                "collection {:?}: a JSON payload cannot be stored in a Kafka-message value \
                 collection",
                self.name
            ))),
        }
    }

    /// Buffers a write of a Kafka message.
    ///
    /// # Errors
    ///
    /// Returns `TransientState` when the collection is a JSON collection;
    /// otherwise a state error carrying the erased category if the write fails.
    pub async fn set_message(
        &self,
        message: Arc<Message>,
        carrier: HashMap<String, String>,
    ) -> Result<(), FfiError> {
        let context = self.propagator.extract(&carrier);
        match &self.state {
            ValueStateVariant::Message(handle) => handle
                .set(message.consumer_message())
                .with_context(context)
                .await
                .map_err(FfiError::from),
            ValueStateVariant::Json(_) => Err(transient_state(format!(
                "collection {:?}: a Kafka-message payload cannot be stored in a JSON value \
                 collection",
                self.name
            ))),
        }
    }

    /// Buffers a clear of the value.
    ///
    /// # Errors
    ///
    /// Returns a state error carrying the erased category if the clear fails.
    pub async fn clear(&self, carrier: HashMap<String, String>) -> Result<(), FfiError> {
        let context = self.propagator.extract(&carrier);
        match &self.state {
            ValueStateVariant::Json(handle) => handle.clear().with_context(context).await,
            ValueStateVariant::Message(handle) => handle.clear().with_context(context).await,
        }
        .map_err(FfiError::from)
    }

    /// Durably commits the buffered operations mid-handler.
    ///
    /// # Errors
    ///
    /// Returns a state error carrying the erased category if the commit fails.
    pub async fn commit(&self, carrier: HashMap<String, String>) -> Result<(), FfiError> {
        let context = self.propagator.extract(&carrier);
        match &self.state {
            ValueStateVariant::Json(handle) => handle.commit().with_context(context).await,
            ValueStateVariant::Message(handle) => handle.commit().with_context(context).await,
        }
        .map_err(FfiError::from)
    }

    /// Discards the buffered uncommitted operations.
    ///
    /// Infallible: rolling back a terminated session is a no-op.
    pub async fn rollback(&self, carrier: HashMap<String, String>) {
        let context = self.propagator.extract(&carrier);
        match &self.state {
            ValueStateVariant::Json(handle) => handle.rollback().with_context(context).await,
            ValueStateVariant::Message(handle) => handle.rollback().with_context(context).await,
        }
    }
}

/// Erased ordered-map state handle, keyed by `String`, vended per event.
#[derive(uniffi::Object)]
pub struct MapStateHandle {
    /// The registered collection name, named in caller-mistake errors.
    pub(crate) name: String,
    /// The wrapped erased map handle.
    pub(crate) state: MapStateVariant,
    /// The propagator used to re-establish the event parent per operation.
    pub(crate) propagator: Arc<TextMapCompositePropagator>,
}

#[uniffi::export(async_runtime = "tokio")]
impl MapStateHandle {
    /// Reads the value for `key`.
    ///
    /// # Errors
    ///
    /// Returns a state error carrying the erased category if the read fails.
    pub async fn get(
        &self,
        key: String,
        carrier: HashMap<String, String>,
    ) -> Result<Option<StateItem>, FfiError> {
        let context = self.propagator.extract(&carrier);
        match &self.state {
            MapStateVariant::Json(handle) => handle
                .get(key)
                .with_context(context)
                .await
                .map(|item| item.map(json_item))
                .map_err(FfiError::from),
            MapStateVariant::Message(handle) => handle
                .get(key)
                .with_context(context)
                .await
                .map(|item| item.map(message_item))
                .map_err(FfiError::from),
        }
    }

    /// Reads several keys in a single isolated batch, in the order requested.
    ///
    /// # Errors
    ///
    /// Returns a state error carrying the erased category if the read fails.
    pub async fn get_many(
        &self,
        keys: Vec<String>,
        carrier: HashMap<String, String>,
    ) -> Result<Vec<Option<StateItem>>, FfiError> {
        let context = self.propagator.extract(&carrier);
        match &self.state {
            MapStateVariant::Json(handle) => handle
                .get_many(keys)
                .with_context(context)
                .await
                .map(|items| items.into_iter().map(|item| item.map(json_item)).collect())
                .map_err(FfiError::from),
            MapStateVariant::Message(handle) => handle
                .get_many(keys)
                .with_context(context)
                .await
                .map(|items| {
                    items
                        .into_iter()
                        .map(|item| item.map(message_item))
                        .collect()
                })
                .map_err(FfiError::from),
        }
    }

    /// Reads whether a stored cell exists for `key`, through this event's dirty
    /// overlay, without decoding the value or running the resolver.
    ///
    /// Presence is payload-agnostic, so both variants delegate to the same
    /// erased primitive: a message-backed map can answer `true` even when the
    /// referenced Kafka message can no longer be fetched. Cheaper than a full
    /// read, but not free — a cache miss still touches the store.
    ///
    /// # Errors
    ///
    /// Returns a state error carrying the erased category if the read fails.
    pub async fn contains_key(
        &self,
        key: String,
        carrier: HashMap<String, String>,
    ) -> Result<bool, FfiError> {
        let context = self.propagator.extract(&carrier);
        match &self.state {
            MapStateVariant::Json(handle) => handle.contains_key(key).with_context(context).await,
            MapStateVariant::Message(handle) => {
                handle.contains_key(key).with_context(context).await
            }
        }
        .map_err(FfiError::from)
    }

    /// Opens a demand-driven cursor over the live entry keys in key order,
    /// skipping every value decode and the resolver.
    ///
    /// Synchronous — it performs no I/O. Mirrors [`Self::scan`], but yields
    /// bare keys: both variants build the same payload-agnostic key cursor,
    /// so a message-backed map enumerates keys with zero Kafka fetches. The
    /// extracted C# context is active while core constructs its stream
    /// span.
    #[must_use]
    pub fn scan_keys(
        &self,
        direction: ScanDirection,
        carrier: HashMap<String, String>,
    ) -> Arc<StateCursor> {
        let context = OwnedCarrier(carrier).into_context(&self.propagator);
        let _guard = context.attach();
        let cursor = match &self.state {
            MapStateVariant::Json(handle) => CursorVariant::MapKeys(handle.keys(direction.into())),
            MapStateVariant::Message(handle) => {
                CursorVariant::MapKeys(handle.keys(direction.into()))
            }
        };
        Arc::new(StateCursor {
            cursor,
            propagator: Arc::clone(&self.propagator),
        })
    }

    /// Inserts or overwrites `key` with a JSON document.
    ///
    /// # Errors
    ///
    /// Returns `TransientState` when the document is JSON `null` (use
    /// `RemoveAsync` to remove the entry) or when the collection is a
    /// Kafka-message collection; otherwise a state error carrying the erased
    /// category if the write fails.
    pub async fn set_json(
        &self,
        key: String,
        bytes: Vec<u8>,
        carrier: HashMap<String, String>,
    ) -> Result<(), FfiError> {
        let context = self.propagator.extract(&carrier);
        match &self.state {
            MapStateVariant::Json(handle) => {
                let payload = BinaryPayload::new(bytes, None::<String>, None::<String>);
                reject_null(
                    &payload,
                    &self.name,
                    "; use RemoveAsync to remove the entry",
                )?;
                handle
                    .set(key, payload)
                    .with_context(context)
                    .await
                    .map_err(FfiError::from)
            }
            MapStateVariant::Message(_) => Err(transient_state(format!(
                "collection {:?}: a JSON payload cannot be stored in a Kafka-message map \
                 collection",
                self.name
            ))),
        }
    }

    /// Inserts or overwrites `key` with a Kafka message.
    ///
    /// # Errors
    ///
    /// Returns `TransientState` when the collection is a JSON collection;
    /// otherwise a state error carrying the erased category if the write fails.
    pub async fn set_message(
        &self,
        key: String,
        message: Arc<Message>,
        carrier: HashMap<String, String>,
    ) -> Result<(), FfiError> {
        let context = self.propagator.extract(&carrier);
        match &self.state {
            MapStateVariant::Message(handle) => handle
                .set(key, message.consumer_message())
                .with_context(context)
                .await
                .map_err(FfiError::from),
            MapStateVariant::Json(_) => Err(transient_state(format!(
                "collection {:?}: a Kafka-message payload cannot be stored in a JSON map \
                 collection",
                self.name
            ))),
        }
    }

    /// Removes `key`.
    ///
    /// # Errors
    ///
    /// Returns a state error carrying the erased category if the removal fails.
    pub async fn remove(
        &self,
        key: String,
        carrier: HashMap<String, String>,
    ) -> Result<(), FfiError> {
        let context = self.propagator.extract(&carrier);
        match &self.state {
            MapStateVariant::Json(handle) => handle.remove(key).with_context(context).await,
            MapStateVariant::Message(handle) => handle.remove(key).with_context(context).await,
        }
        .map_err(FfiError::from)
    }

    /// Removes every entry.
    ///
    /// # Errors
    ///
    /// Returns a state error carrying the erased category if the clear fails.
    pub async fn clear(&self, carrier: HashMap<String, String>) -> Result<(), FfiError> {
        let context = self.propagator.extract(&carrier);
        match &self.state {
            MapStateVariant::Json(handle) => handle.clear().with_context(context).await,
            MapStateVariant::Message(handle) => handle.clear().with_context(context).await,
        }
        .map_err(FfiError::from)
    }

    /// Opens a demand-driven cursor over the live entries in key order.
    ///
    /// Synchronous — it performs no I/O. The extracted C# context is active
    /// while core constructs its semantic stream span; chunk pulls open no
    /// binding span.
    #[must_use]
    pub fn scan(
        &self,
        direction: ScanDirection,
        carrier: HashMap<String, String>,
    ) -> Arc<StateCursor> {
        let context = OwnedCarrier(carrier).into_context(&self.propagator);
        let _guard = context.attach();
        let cursor = match &self.state {
            MapStateVariant::Json(handle) => CursorVariant::MapJson(handle.scan(direction.into())),
            MapStateVariant::Message(handle) => {
                CursorVariant::MapMessage(handle.scan(direction.into()))
            }
        };
        Arc::new(StateCursor {
            cursor,
            propagator: Arc::clone(&self.propagator),
        })
    }

    /// Durably commits the buffered operations mid-handler.
    ///
    /// # Errors
    ///
    /// Returns a state error carrying the erased category if the commit fails.
    pub async fn commit(&self, carrier: HashMap<String, String>) -> Result<(), FfiError> {
        let context = self.propagator.extract(&carrier);
        match &self.state {
            MapStateVariant::Json(handle) => handle.commit().with_context(context).await,
            MapStateVariant::Message(handle) => handle.commit().with_context(context).await,
        }
        .map_err(FfiError::from)
    }

    /// Discards the buffered uncommitted operations.
    ///
    /// Infallible: rolling back a terminated session is a no-op.
    pub async fn rollback(&self, carrier: HashMap<String, String>) {
        let context = self.propagator.extract(&carrier);
        match &self.state {
            MapStateVariant::Json(handle) => handle.rollback().with_context(context).await,
            MapStateVariant::Message(handle) => handle.rollback().with_context(context).await,
        }
    }
}

/// Erased deque state handle, vended per event.
#[derive(uniffi::Object)]
pub struct DequeStateHandle {
    /// The registered collection name, named in caller-mistake errors.
    pub(crate) name: String,
    /// The wrapped erased deque handle.
    pub(crate) state: DequeStateVariant,
    /// The propagator used to re-establish the event parent per operation.
    pub(crate) propagator: Arc<TextMapCompositePropagator>,
}

#[uniffi::export(async_runtime = "tokio")]
impl DequeStateHandle {
    /// The number of live elements.
    ///
    /// # Errors
    ///
    /// Returns a state error carrying the erased category if the read fails.
    pub async fn len(&self, carrier: HashMap<String, String>) -> Result<u64, FfiError> {
        let context = self.propagator.extract(&carrier);
        let length = match &self.state {
            DequeStateVariant::Json(handle) => handle.len().with_context(context).await,
            DequeStateVariant::Message(handle) => handle.len().with_context(context).await,
        }
        .map_err(FfiError::from)?;
        Ok(length as u64)
    }

    /// Whether the deque holds no live elements.
    ///
    /// # Errors
    ///
    /// Returns a state error carrying the erased category if the read fails.
    pub async fn is_empty(&self, carrier: HashMap<String, String>) -> Result<bool, FfiError> {
        let context = self.propagator.extract(&carrier);
        match &self.state {
            DequeStateVariant::Json(handle) => handle.is_empty().with_context(context).await,
            DequeStateVariant::Message(handle) => handle.is_empty().with_context(context).await,
        }
        .map_err(FfiError::from)
    }

    /// Reads the element at front-relative position `index`.
    ///
    /// An index past the end reads as `None` (core semantics); an index beyond
    /// the addressable range is clamped, still reading `None`.
    ///
    /// # Errors
    ///
    /// Returns a state error carrying the erased category if the read fails.
    pub async fn get(
        &self,
        index: u64,
        carrier: HashMap<String, String>,
    ) -> Result<Option<StateItem>, FfiError> {
        let context = self.propagator.extract(&carrier);
        let index = usize::try_from(index).unwrap_or(usize::MAX);
        match &self.state {
            DequeStateVariant::Json(handle) => handle
                .get(index)
                .with_context(context)
                .await
                .map(|item| item.map(json_item))
                .map_err(FfiError::from),
            DequeStateVariant::Message(handle) => handle
                .get(index)
                .with_context(context)
                .await
                .map(|item| item.map(message_item))
                .map_err(FfiError::from),
        }
    }

    /// Appends a JSON document at the back.
    ///
    /// # Errors
    ///
    /// Returns `TransientState` when the document is JSON `null` or when the
    /// collection is a Kafka-message collection; otherwise a state error
    /// carrying the erased category if the write fails.
    pub async fn push_back_json(
        &self,
        bytes: Vec<u8>,
        carrier: HashMap<String, String>,
    ) -> Result<(), FfiError> {
        let context = self.propagator.extract(&carrier);
        match &self.state {
            DequeStateVariant::Json(handle) => {
                let payload = BinaryPayload::new(bytes, None::<String>, None::<String>);
                reject_null(&payload, &self.name, " in a deque")?;
                handle
                    .push_back(payload)
                    .with_context(context)
                    .await
                    .map_err(FfiError::from)
            }
            DequeStateVariant::Message(_) => Err(transient_state(format!(
                "collection {:?}: a JSON payload cannot be stored in a Kafka-message deque \
                 collection",
                self.name
            ))),
        }
    }

    /// Appends a Kafka message at the back.
    ///
    /// # Errors
    ///
    /// Returns `TransientState` when the collection is a JSON collection;
    /// otherwise a state error carrying the erased category if the write fails.
    pub async fn push_back_message(
        &self,
        message: Arc<Message>,
        carrier: HashMap<String, String>,
    ) -> Result<(), FfiError> {
        let context = self.propagator.extract(&carrier);
        match &self.state {
            DequeStateVariant::Message(handle) => handle
                .push_back(message.consumer_message())
                .with_context(context)
                .await
                .map_err(FfiError::from),
            DequeStateVariant::Json(_) => Err(transient_state(format!(
                "collection {:?}: a Kafka-message payload cannot be stored in a JSON deque \
                 collection",
                self.name
            ))),
        }
    }

    /// Prepends a JSON document at the front.
    ///
    /// # Errors
    ///
    /// Returns `TransientState` when the document is JSON `null` or when the
    /// collection is a Kafka-message collection; otherwise a state error
    /// carrying the erased category if the write fails.
    pub async fn push_front_json(
        &self,
        bytes: Vec<u8>,
        carrier: HashMap<String, String>,
    ) -> Result<(), FfiError> {
        let context = self.propagator.extract(&carrier);
        match &self.state {
            DequeStateVariant::Json(handle) => {
                let payload = BinaryPayload::new(bytes, None::<String>, None::<String>);
                reject_null(&payload, &self.name, " in a deque")?;
                handle
                    .push_front(payload)
                    .with_context(context)
                    .await
                    .map_err(FfiError::from)
            }
            DequeStateVariant::Message(_) => Err(transient_state(format!(
                "collection {:?}: a JSON payload cannot be stored in a Kafka-message deque \
                 collection",
                self.name
            ))),
        }
    }

    /// Prepends a Kafka message at the front.
    ///
    /// # Errors
    ///
    /// Returns `TransientState` when the collection is a JSON collection;
    /// otherwise a state error carrying the erased category if the write fails.
    pub async fn push_front_message(
        &self,
        message: Arc<Message>,
        carrier: HashMap<String, String>,
    ) -> Result<(), FfiError> {
        let context = self.propagator.extract(&carrier);
        match &self.state {
            DequeStateVariant::Message(handle) => handle
                .push_front(message.consumer_message())
                .with_context(context)
                .await
                .map_err(FfiError::from),
            DequeStateVariant::Json(_) => Err(transient_state(format!(
                "collection {:?}: a Kafka-message payload cannot be stored in a JSON deque \
                 collection",
                self.name
            ))),
        }
    }

    /// Removes and returns the front element.
    ///
    /// # Errors
    ///
    /// Returns a state error carrying the erased category if the operation
    /// fails.
    pub async fn pop_front(
        &self,
        carrier: HashMap<String, String>,
    ) -> Result<Option<StateItem>, FfiError> {
        let context = self.propagator.extract(&carrier);
        match &self.state {
            DequeStateVariant::Json(handle) => handle
                .pop_front()
                .with_context(context)
                .await
                .map(|item| item.map(json_item))
                .map_err(FfiError::from),
            DequeStateVariant::Message(handle) => handle
                .pop_front()
                .with_context(context)
                .await
                .map(|item| item.map(message_item))
                .map_err(FfiError::from),
        }
    }

    /// Removes and returns the back element.
    ///
    /// # Errors
    ///
    /// Returns a state error carrying the erased category if the operation
    /// fails.
    pub async fn pop_back(
        &self,
        carrier: HashMap<String, String>,
    ) -> Result<Option<StateItem>, FfiError> {
        let context = self.propagator.extract(&carrier);
        match &self.state {
            DequeStateVariant::Json(handle) => handle
                .pop_back()
                .with_context(context)
                .await
                .map(|item| item.map(json_item))
                .map_err(FfiError::from),
            DequeStateVariant::Message(handle) => handle
                .pop_back()
                .with_context(context)
                .await
                .map(|item| item.map(message_item))
                .map_err(FfiError::from),
        }
    }

    /// Reads the front element without a length round trip.
    ///
    /// An endpoint-slot read — exactly `get(0)` minus the length read. Under a
    /// TTL an expired front slot yields `None` even when live interior elements
    /// remain; a peek never searches inward.
    ///
    /// # Errors
    ///
    /// Returns a state error carrying the erased category if the read fails.
    pub async fn peek_front(
        &self,
        carrier: HashMap<String, String>,
    ) -> Result<Option<StateItem>, FfiError> {
        let context = self.propagator.extract(&carrier);
        match &self.state {
            DequeStateVariant::Json(handle) => handle
                .peek_front()
                .with_context(context)
                .await
                .map(|item| item.map(json_item))
                .map_err(FfiError::from),
            DequeStateVariant::Message(handle) => handle
                .peek_front()
                .with_context(context)
                .await
                .map(|item| item.map(message_item))
                .map_err(FfiError::from),
        }
    }

    /// Reads the back element without a length round trip.
    ///
    /// An endpoint-slot read — exactly `get(len - 1)` minus the length read,
    /// and safe on an empty deque (returns `None`). TTL-hole semantics
    /// match [`Self::peek_front`].
    ///
    /// # Errors
    ///
    /// Returns a state error carrying the erased category if the read fails.
    pub async fn peek_back(
        &self,
        carrier: HashMap<String, String>,
    ) -> Result<Option<StateItem>, FfiError> {
        let context = self.propagator.extract(&carrier);
        match &self.state {
            DequeStateVariant::Json(handle) => handle
                .peek_back()
                .with_context(context)
                .await
                .map(|item| item.map(json_item))
                .map_err(FfiError::from),
            DequeStateVariant::Message(handle) => handle
                .peek_back()
                .with_context(context)
                .await
                .map(|item| item.map(message_item))
                .map_err(FfiError::from),
        }
    }

    /// Removes every element.
    ///
    /// # Errors
    ///
    /// Returns a state error carrying the erased category if the clear fails.
    pub async fn clear(&self, carrier: HashMap<String, String>) -> Result<(), FfiError> {
        let context = self.propagator.extract(&carrier);
        match &self.state {
            DequeStateVariant::Json(handle) => handle.clear().with_context(context).await,
            DequeStateVariant::Message(handle) => handle.clear().with_context(context).await,
        }
        .map_err(FfiError::from)
    }

    /// Opens a demand-driven cursor over the live elements in index order.
    ///
    /// Synchronous — it performs no I/O. The extracted C# context is active
    /// while core constructs its semantic stream span; chunk pulls open no
    /// binding span.
    #[must_use]
    pub fn scan(
        &self,
        direction: ScanDirection,
        carrier: HashMap<String, String>,
    ) -> Arc<StateCursor> {
        let context = OwnedCarrier(carrier).into_context(&self.propagator);
        let _guard = context.attach();
        let cursor = match &self.state {
            DequeStateVariant::Json(handle) => {
                CursorVariant::DequeJson(handle.scan(direction.into()))
            }
            DequeStateVariant::Message(handle) => {
                CursorVariant::DequeMessage(handle.scan(direction.into()))
            }
        };
        Arc::new(StateCursor {
            cursor,
            propagator: Arc::clone(&self.propagator),
        })
    }

    /// Durably commits the buffered operations mid-handler.
    ///
    /// # Errors
    ///
    /// Returns a state error carrying the erased category if the commit fails.
    pub async fn commit(&self, carrier: HashMap<String, String>) -> Result<(), FfiError> {
        let context = self.propagator.extract(&carrier);
        match &self.state {
            DequeStateVariant::Json(handle) => handle.commit().with_context(context).await,
            DequeStateVariant::Message(handle) => handle.commit().with_context(context).await,
        }
        .map_err(FfiError::from)
    }

    /// Discards the buffered uncommitted operations.
    ///
    /// Infallible: rolling back a terminated session is a no-op.
    pub async fn rollback(&self, carrier: HashMap<String, String>) {
        let context = self.propagator.extract(&carrier);
        match &self.state {
            DequeStateVariant::Json(handle) => handle.rollback().with_context(context).await,
            DequeStateVariant::Message(handle) => handle.rollback().with_context(context).await,
        }
    }
}

/// Demand-driven scan cursor over a map or deque collection.
///
/// Pulling is lazy: each [`Self::next_chunk`] restores the C# context without
/// opening a binding span, awaits one stream item, and asks core to drain only
/// the immediately-ready tail. Chunking, exhaustion, error ordering,
/// serialization, close-idempotence, and use-after-close behaviour are
/// core-owned; this layer only transports.
#[derive(uniffi::Object)]
pub struct StateCursor {
    /// The wrapped erased cursor.
    pub(crate) cursor: CursorVariant,
    /// The propagator used to re-establish the event parent per pull.
    pub(crate) propagator: Arc<TextMapCompositePropagator>,
}

#[uniffi::export(async_runtime = "tokio")]
impl StateCursor {
    /// Pulls the next immediately-ready chunk of scanned items.
    ///
    /// Delegates directly to core's ready-chunk draining, transporting the
    /// returned vector unchanged. Returns `None` once the scan is exhausted.
    ///
    /// # Errors
    ///
    /// Returns a state error carrying the erased category if the pull fails or
    /// the cursor was closed.
    pub async fn next_chunk(
        &self,
        carrier: HashMap<String, String>,
    ) -> Result<Option<Vec<StateScanItem>>, FfiError> {
        let context = self.propagator.extract(&carrier);
        match &self.cursor {
            CursorVariant::DequeJson(cursor) => cursor
                .next_ready_chunk(SCAN_READY_CHUNK_SIZE)
                .with_context(context)
                .await
                .map(|chunk| {
                    chunk.map(|items| {
                        items
                            .into_iter()
                            .map(|payload| StateScanItem::DequeJson {
                                bytes: payload.bytes,
                            })
                            .collect()
                    })
                })
                .map_err(FfiError::from),
            CursorVariant::MapJson(cursor) => cursor
                .next_ready_chunk(SCAN_READY_CHUNK_SIZE)
                .with_context(context)
                .await
                .map(|chunk| {
                    chunk.map(|items| {
                        items
                            .into_iter()
                            .map(|(key, payload)| StateScanItem::MapJson {
                                key,
                                bytes: payload.bytes,
                            })
                            .collect()
                    })
                })
                .map_err(FfiError::from),
            CursorVariant::DequeMessage(cursor) => cursor
                .next_ready_chunk(SCAN_READY_CHUNK_SIZE)
                .with_context(context)
                .await
                .map(|chunk| {
                    chunk.map(|items| {
                        items
                            .into_iter()
                            .map(|message| StateScanItem::DequeMessage {
                                message: Arc::new(Message::new(message)),
                            })
                            .collect()
                    })
                })
                .map_err(FfiError::from),
            CursorVariant::MapMessage(cursor) => cursor
                .next_ready_chunk(SCAN_READY_CHUNK_SIZE)
                .with_context(context)
                .await
                .map(|chunk| {
                    chunk.map(|items| {
                        items
                            .into_iter()
                            .map(|(key, message)| StateScanItem::MapMessage {
                                key,
                                message: Arc::new(Message::new(message)),
                            })
                            .collect()
                    })
                })
                .map_err(FfiError::from),
            CursorVariant::MapKeys(cursor) => cursor
                .next_ready_chunk(SCAN_READY_CHUNK_SIZE)
                .with_context(context)
                .await
                .map(|chunk| {
                    chunk.map(|keys| {
                        keys.into_iter()
                            .map(|key| StateScanItem::MapKey { key })
                            .collect()
                    })
                })
                .map_err(FfiError::from),
        }
    }

    /// Closes the cursor, releasing the underlying stream.
    ///
    /// Idempotent; a subsequent [`Self::next_chunk`] errors. No span — pure
    /// teardown.
    pub async fn close(&self) {
        match &self.cursor {
            CursorVariant::DequeJson(cursor) => cursor.close().await,
            CursorVariant::MapJson(cursor) => cursor.close().await,
            CursorVariant::DequeMessage(cursor) => cursor.close().await,
            CursorVariant::MapMessage(cursor) => cursor.close().await,
            CursorVariant::MapKeys(cursor) => cursor.close().await,
        }
    }
}
