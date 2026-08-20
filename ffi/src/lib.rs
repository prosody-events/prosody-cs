#![recursion_limit = "256"]
//! Prosody FFI bindings for C#.
//!
//! This crate provides FFI bindings for the Prosody Kafka client library,
//! enabling C# applications to use Prosody's event-driven message processing
//! capabilities. `BoltFFI` generates the C# bindings.
//!
//! # Building
//!
//! ```bash
//! # Build the cdylib (produces libprosody_ffi.dylib/.so/.dll)
//! cargo build -p prosody_ffi --release
//!
//! # Generate C# bindings
//! boltffi generate csharp --deny-skipped
//! ```
//!
//! # Architecture
//!
//! This crate serves as the FFI boundary layer. C# code wraps the generated
//! bindings in idiomatic classes that provide:
//! - Typed JSON payloads via `Send<T>()` and `GetPayload<T>()`
//! - `CancellationToken` support on async methods
//! - Properties instead of methods for simple accessors
//!
//! # Modules
//!
//! - [`admin`]: Admin client for Kafka topic management (create, delete)
//! - [`client`]: Core [`ProsodyClient`] service implementation
//! - [`config`]: Configuration conversion utilities for builder types
//! - [`context`]: Event context for timer scheduling and cancellation checks
//! - [`error`]: Error types that cross the FFI boundary
//! - [`handler`]: [`EventHandler`] callback trait for message/timer processing
//! - [`logging`]: Logging bridge from Rust tracing to C# `ILoggerFactory`
//! - [`message`]: Kafka message wrapper for C# consumption
//! - [`state`]: Shared keyed-state types and validation
//! - [`timer`]: Timer trigger wrapper for scheduled event handling
//! - [`types`]: Configuration records ([`ClientOptions`], [`ClientMode`])

use mimalloc::MiMalloc;

#[global_allocator]
static GLOBAL: MiMalloc = MiMalloc;

use std::collections::HashMap;

pub mod admin;
pub mod client;
pub mod config;
pub mod context;
pub mod cursor;
pub mod error;
pub mod event;
pub mod handler;
pub mod json_deque;
pub mod logging;
pub mod map;
pub mod message;
pub mod message_deque;
pub mod published;
pub(crate) mod runtime;
pub mod state;
pub mod timer;
pub mod types;
pub mod value;

/// OpenTelemetry context carrier for distributed tracing propagation.
///
/// This type alias is used to pass trace context (trace ID, span ID, etc.)
/// across the FFI boundary. Rust injects context into the carrier before
/// calling C# handlers, and C# injects context before calling Rust methods.
///
/// In C#, this maps to `IDictionary<string, string>`.
pub type Carrier = HashMap<String, String>;

// Re-export the FFI types from the crate root.

pub use admin::AdminClient;
pub use client::ProsodyClient;
pub use context::Context;
pub use cursor::{
    JsonDequeCursor, JsonMapCursor, JsonMapEntry, MapKeyCursor, MessageDequeCursor,
    MessageMapCursor,
};
pub use error::FfiError;
pub use event::NativeEvent;
pub use handler::{
    EventHandler, HandlerResultCode, NativeExciseRequest, NativeRequest, NativeRequestResult,
};
pub use json_deque::JsonDequeStateHandle;
pub use map::{JsonMapStateHandle, JsonMapValue, MessageMapStateHandle};
pub use message::{ExciseMessage, Message, MessageBatch};
pub use message_deque::MessageDequeStateHandle;
pub use published::{PublishedDequeHandle, PublishedMapHandle, PublishedValueHandle};
pub use state::ScanDirection;
pub use timer::Timer;
pub use types::{
    ClientMode, ClientOptions, ConsumerState, StateCollectionConfig, StateKind, StatePayload,
};
pub use value::{JsonValueStateHandle, MessageValueStateHandle};
