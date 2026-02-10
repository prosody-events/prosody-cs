//! Prosody FFI bindings for C#.
//!
//! This crate provides FFI bindings for the Prosody Kafka client library,
//! enabling C# applications to use Prosody's event-driven message processing
//! capabilities. Bindings are generated automatically using `UniFFI` via
//! uniffi-bindgen-cs.
//!
//! # Building
//!
//! ```bash
//! # Build the cdylib (produces libprosody_ffi.dylib/.so/.dll)
//! cargo build -p prosody-ffi --release
//!
//! # Generate C# bindings (via uniffi-bindgen-cs)
//! uniffi-bindgen-cs --library target/release/libprosody_ffi.dylib -o src/Prosody/Generated
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
//! - [`cancellation`]: Cancellation signaling for cooperative async
//!   cancellation
//! - [`client`]: Core [`ProsodyClient`] service implementation
//! - [`config`]: Configuration conversion utilities for builder types
//! - [`context`]: Event context for timer scheduling and cancellation checks
//! - [`error`]: Error types that cross the FFI boundary
//! - [`handler`]: [`EventHandler`] callback trait for message/timer processing
//! - [`logging`]: Logging bridge from Rust tracing to C# `ILoggerFactory`
//! - [`message`]: Kafka message wrapper for C# consumption
//! - [`timer`]: Timer trigger wrapper for scheduled event handling
//! - [`types`]: Configuration records ([`ClientOptions`], [`ClientMode`])

#[cfg(not(target_env = "msvc"))]
use tikv_jemallocator::Jemalloc;

#[cfg(not(target_env = "msvc"))]
#[global_allocator]
static GLOBAL: Jemalloc = Jemalloc;

use std::collections::HashMap;

pub mod admin;
pub mod cancellation;
pub mod client;
pub mod config;
pub mod context;
pub mod error;
pub mod handler;
pub mod logging;
pub mod message;
pub mod timer;
pub mod types;

/// OpenTelemetry context carrier for distributed tracing propagation.
///
/// This type alias is used to pass trace context (trace ID, span ID, etc.)
/// across the FFI boundary. Rust injects context into the carrier before
/// calling C# handlers, and C# injects context before calling Rust methods.
///
/// In C#, this maps to `IDictionary<string, string>`.
pub type Carrier = HashMap<String, String>;

// Re-exports for UniFFI scaffolding.
//
// UniFFI discovers exported types through the crate root. These re-exports
// ensure all public FFI types are visible for binding generation.

pub use admin::AdminClient;
pub use cancellation::CancellationSignal;
pub use client::ProsodyClient;
pub use context::Context;
pub use error::FfiError;
pub use handler::{EventHandler, HandlerResultCode};
pub use message::Message;
pub use timer::Timer;
pub use types::{ClientMode, ClientOptions, ConsumerState};

// Initialize UniFFI scaffolding (proc-macro approach, no UDL file required).
uniffi::setup_scaffolding!();
