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
//! - [`error`]: Error types for FFI boundary crossing
//! - [`events`]: Message and Timer event types passed to C# handlers
//! - [`handler`]: `EventHandler` trait for FFI callback interface
//! - [`client`]: `ProsodyClient` service implementation
//! - [`types`]: Configuration types (`ClientOptions`)

pub mod admin;
pub mod client;
pub mod config;
pub mod error;
pub mod events;
pub mod handler;
pub mod types;

// Re-export key types for the UDL interface
pub use admin::AdminClient;
pub use client::ProsodyClient;
pub use error::FfiError;
pub use events::{CancellationSignal, Context, Message, Timer};
pub use handler::{EventHandler, HandlerResultCode};
pub use types::{ClientMode, ClientOptions, ConsumerState};

// Setup UniFFI scaffolding using proc-macro approach (no UDL file)
uniffi::setup_scaffolding!();
