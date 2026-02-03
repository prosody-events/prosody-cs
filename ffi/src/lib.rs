//! Prosody FFI bindings for C#.
//!
//! This crate provides FFI bindings for the Prosody Kafka client library,
//! using `UniFFI` for automatic C# bindings generation via uniffi-bindgen-cs.
//!
//! ## Building
//!
//! ```bash
//! # Build the cdylib (produces libprosody_ffi.dylib/.so/.dll)
//! cargo build -p prosody-ffi --release
//!
//! # Generate C# bindings (via uniffi-bindgen-cs)
//! uniffi-bindgen-cs --library target/release/libprosody_ffi.dylib -o src/Prosody/Generated
//! ```
//!
//! ## Module Organization
//!
//! - [`admin`]: Admin client for topic management
//! - [`cancellation`]: Cancellation signaling for async operations
//! - [`client`]: `ProsodyClient` service implementation
//! - [`config`]: Configuration types
//! - [`context`]: Event context for scheduling and cancellation
//! - [`error`]: Error types for FFI boundary crossing
//! - [`handler`]: `EventHandler` trait for FFI callback interface
//! - [`message`]: Kafka message wrapper
//! - [`timer`]: Timer trigger wrapper
//! - [`types`]: Configuration types (`ClientOptions`)

pub mod admin;
pub mod cancellation;
pub mod client;
pub mod config;
pub mod context;
pub mod error;
pub mod handler;
pub mod message;
pub mod timer;
pub mod types;

// Re-export key types for the UDL interface
pub use admin::AdminClient;
pub use cancellation::CancellationSignal;
pub use client::ProsodyClient;
pub use context::Context;
pub use error::FfiError;
pub use handler::{EventHandler, HandlerResultCode};
pub use message::Message;
pub use timer::{Carrier, Timer};
pub use types::{ClientMode, ClientOptions, ConsumerState};

// Setup UniFFI scaffolding using proc-macro approach (no UDL file)
uniffi::setup_scaffolding!();
