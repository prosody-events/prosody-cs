// Interoptopus #[ffi_service] macro generates FFI glue code requiring unsafe, no_mangle, and internal structs.
// These warnings cannot be avoided when using the service pattern.
#![expect(
    unsafe_code,
    clippy::ignored_unit_patterns,
    missing_docs,
    reason = "Interoptopus #[ffi_service] macro generates FFI glue code"
)]

//! `ProsodyClientService` - Main FFI service for the Prosody client.
//!
//! This module exposes the prosody `HighLevelClient` to C# via Interoptopus's
//! service pattern. The service provides an object-oriented API that maps
//! naturally to C# classes.
//!
//! # Usage
//!
//! In C#, the generated class looks like:
//! ```csharp
//! using var client = new ProsodyClientService(options);
//! await client.SendAsync(topic, key, payload);
//! await client.SubscribeAsync(handler);
//! ```
//!
//! # Reference
//!
//! - prosody-js: `src/client/mod.rs`
//! - prosody-py: `src/client/mod.rs`
//! - prosody-rb: `ext/prosody/src/client/mod.rs`

use crate::callbacks::CSharpHandler;
use crate::error::FFIErrorCode;
use crate::runtime::GlobalTokio;
use crate::types::{ConsumerState, FFIClientOptions};
use crate::RUNTIME;
use interoptopus::{ffi, ffi_service, ffi_type, AsyncRuntime};
use prosody::high_level::state::ConsumerState as ProsodyConsumerState;
use prosody::high_level::HighLevelClient;
use std::sync::Arc;
use tokio::sync::Mutex;

/// Main Prosody client service exposed to C#.
///
/// This service wraps the prosody `HighLevelClient` and provides:
/// - Message sending to Kafka topics
/// - Subscribing to topics with handlers
/// - Consumer state management
/// - Stall detection
///
/// # Lifecycle
///
/// ```text
///       ┌──────────┐
///       │  Created │
///       └────┬─────┘
///            │ new()
///            ▼
///       ┌──────────┐
///       │   Idle   │◄────────────────┐
///       └────┬─────┘                 │
///            │ subscribe()           │ unsubscribe()
///            ▼                       │
///       ┌──────────┐                 │
///       │Subscribed├─────────────────┘
///       └────┬─────┘
///            │ drop / dispose
///            ▼
///       ┌──────────┐
///       │ Disposed │
///       └──────────┘
/// ```
#[derive(AsyncRuntime)]
#[ffi_type(opaque)]
pub struct ProsodyClientService {
    /// Runtime wrapper for Interoptopus async support (zero-sized, uses global RUNTIME).
    runtime: GlobalTokio,
    /// The wrapped high-level client.
    /// Uses `CSharpHandler` as the handler type.
    client: Arc<HighLevelClient<CSharpHandler>>,
    /// Handler registration state (for managing callback lifetime).
    handler: Mutex<Option<Arc<CSharpHandler>>>,
}

#[ffi_service]
impl ProsodyClientService {
    /// Creates a new `ProsodyClientService` with the given options.
    ///
    /// # Arguments
    ///
    /// * `options` - Configuration options for the client
    ///
    /// # Errors
    ///
    /// Returns error if the client cannot connect to Kafka or options are invalid.
    #[must_use]
    pub fn new_with(options: &FFIClientOptions) -> ffi::Result<Self, FFIErrorCode> {
        // TODO: Convert FFIClientOptions to prosody builder
        // For now, return an error indicating not yet implemented
        let _ = options; // Suppress unused warning until implemented
        ffi::Err(FFIErrorCode::Internal)
    }

    /// Returns the current consumer state.
    ///
    /// # Returns
    ///
    /// - `Unconfigured` - No subscription topics configured
    /// - `Configured` - Topics configured but not consuming
    /// - `Running` - Actively consuming messages
    pub fn consumer_state(&self) -> ConsumerState {
        RUNTIME.block_on(async {
            let state_view = self.client.consumer_state().await;
            match &*state_view {
                ProsodyConsumerState::Unconfigured => ConsumerState::Unconfigured,
                ProsodyConsumerState::Configured(_) => ConsumerState::Configured,
                ProsodyConsumerState::Running { .. } => ConsumerState::Running,
            }
        })
    }

    /// Returns the number of partitions currently assigned to this consumer.
    pub fn assigned_partition_count(&self) -> u32 {
        RUNTIME.block_on(async { self.client.assigned_partition_count().await })
    }

    /// Returns `true` if the consumer is currently stalled.
    ///
    /// A consumer is stalled when it has not made progress for longer than
    /// the configured stall threshold.
    pub fn is_stalled(&self) -> bool {
        RUNTIME.block_on(async { self.client.is_stalled().await })
    }

    /// Returns the configured source system identifier.
    ///
    /// The source system identifies the originating service in message headers.
    pub fn source_system(&self) -> ffi::CStrPtr<'_> {
        // TODO: This needs lifetime handling - may need to cache the string
        // For now return an empty pointer
        ffi::CStrPtr::empty()
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn consumer_state_default_is_unconfigured() {
        assert_eq!(ConsumerState::default(), ConsumerState::Unconfigured);
    }
}
