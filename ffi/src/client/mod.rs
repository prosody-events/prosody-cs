//! Low-level client for the C# binding.

use std::collections::HashMap;
use std::sync::Arc;
use std::time::Duration;

use arc_swap::ArcSwap;
use opentelemetry::propagation::TextMapPropagator;
use tracing::field::Empty;
use tracing::{Instrument, debug, info_span};
use tracing_opentelemetry::OpenTelemetrySpanExt;

use crate::config::{
    build_cassandra_config, build_consumer_builders, build_producer_config, get_mode,
};
use crate::error::FfiError;
use crate::event::{EventRegistry, NativeEvent};
use crate::handler::{
    CsHandler, EventHandler, NativeExciseRequest, NativeRequest, NativeRequestResult,
};
use crate::logging::ensure_tracing_initialized;
use crate::published::{PublishedDequeHandle, PublishedMapHandle, PublishedValueHandle};
use crate::types::{ClientOptions, ConsumerState, EventMetadata};
use prosody::codec::BinaryPayload;
use prosody::high_level::erased::{
    ErasedConsumerState, ErasedReadCache, SharedHighLevelClient, new_erased,
};
use prosody::propagator::new_propagator;
use prosody::requester::ResponseError;
use prosody::subsystem::SubsystemName;

mod outcome;

use outcome::native_request_result;

fn read_cache(ttl: Option<Duration>, disabled: bool) -> Result<ErasedReadCache, FfiError> {
    match (ttl, disabled) {
        (Some(_), true) => Err(FfiError::PermanentState(
            "read cache cannot set both a TTL and disabled".to_owned(),
        )),
        (None, true) => Ok(ErasedReadCache::Disabled),
        (Some(ttl), false) => Ok(ErasedReadCache::Ttl(ttl)),
        (None, false) => Ok(ErasedReadCache::Inherit),
    }
}

/// Native Prosody client exposed to C# via `BoltFFI`.
///
/// This is the low-level FFI client. C# wraps this in `Prosody.ProsodyClient`
/// which provides typed JSON, `CancellationToken` support, and idiomatic
/// properties.
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
///
/// # Thread Safety
///
/// This type is `Send + Sync` and can be safely shared across threads.
/// The internal state is protected by atomic operations and async-aware locks.
pub struct ProsodyClient {
    /// Underlying prosody high-level client instance.
    client: SharedHighLevelClient<CsHandler>,
    /// Holds the C# handler reference to prevent premature deallocation.
    ///
    /// Uses [`ArcSwap`] for lock-free updates during subscribe/unsubscribe.
    handler: ArcSwap<Option<Arc<dyn EventHandler>>>,
    /// Transfers each callback's resources into one `BoltFFI` class handle.
    registry: Arc<EventRegistry>,
}

/// BoltFFI-exported methods for [`ProsodyClient`].
#[prosody_ffi_macros::ffi_async]
#[boltffi::export]
impl ProsodyClient {
    /// Creates a new client with the specified configuration.
    ///
    /// Initializes the tracing subsystem if not already initialized, then
    /// builds and connects the underlying Kafka producer and consumer.
    ///
    /// # Errors
    ///
    /// Returns [`FfiError::Client`] if:
    /// - Kafka bootstrap servers are unreachable
    /// - Configuration options are invalid
    /// - Cassandra connection fails (when persistence is enabled)
    pub async fn new(options: ClientOptions) -> Result<Self, FfiError> {
        // Ensure tracing is initialized (idempotent)
        ensure_tracing_initialized();

        // Build all configuration from ClientOptions
        let mut producer_config = build_producer_config(&options);
        let consumer_builders = build_consumer_builders(&options)?;
        let event_capacity = consumer_builders
            .scheduler
            .clone()
            .build()
            .map_err(|error| FfiError::Validation(error.to_string()))?
            .max_concurrency;
        let cassandra = build_cassandra_config(&options);
        let mode = get_mode(&options);

        let client = new_erased(mode, &mut producer_config, &consumer_builders, &cassandra).await?;

        Ok(Self {
            client,
            handler: ArcSwap::new(Arc::new(None)),
            registry: Arc::new(EventRegistry::new(event_capacity)),
        })
    }

    /// Takes the resources for one handler callback.
    ///
    /// # Errors
    ///
    /// Returns a transient error if the event ID is not active.
    pub fn take_event(&self, event_id: u64) -> Result<NativeEvent, FfiError> {
        self.registry.take(event_id)
    }

    /// Opens a read-only published value collection.
    ///
    /// # Errors
    ///
    /// Returns a permanent state error when the descriptor cannot be resolved.
    pub async fn published_value(
        &self,
        subsystem: String,
        name: String,
        cache_ttl: Option<Duration>,
        cache_disabled: bool,
    ) -> Result<PublishedValueHandle, FfiError> {
        let reader = self
            .client
            .value_state(subsystem, name, read_cache(cache_ttl, cache_disabled)?)
            .await
            .map_err(|error| FfiError::PermanentState(error.to_string()))?;
        Ok(PublishedValueHandle {
            reader,
            propagator: Arc::new(new_propagator()),
        })
    }

    /// Opens a read-only published map collection.
    ///
    /// # Errors
    ///
    /// Returns a permanent state error when the descriptor cannot be resolved.
    pub async fn published_map(
        &self,
        subsystem: String,
        name: String,
        cache_ttl: Option<Duration>,
        cache_disabled: bool,
    ) -> Result<PublishedMapHandle, FfiError> {
        let reader = self
            .client
            .map_state(subsystem, name, read_cache(cache_ttl, cache_disabled)?)
            .await
            .map_err(|error| FfiError::PermanentState(error.to_string()))?;
        Ok(PublishedMapHandle {
            reader,
            propagator: Arc::new(new_propagator()),
        })
    }

    /// Opens a read-only published deque collection.
    ///
    /// # Errors
    ///
    /// Returns a permanent state error when the descriptor cannot be resolved.
    pub async fn published_deque(
        &self,
        subsystem: String,
        name: String,
        cache_ttl: Option<Duration>,
        cache_disabled: bool,
    ) -> Result<PublishedDequeHandle, FfiError> {
        let reader = self
            .client
            .deque_state(subsystem, name, read_cache(cache_ttl, cache_disabled)?)
            .await
            .map_err(|error| FfiError::PermanentState(error.to_string()))?;
        Ok(PublishedDequeHandle {
            reader,
            propagator: Arc::new(new_propagator()),
        })
    }

    /// Subscribes to configured topics and begins consuming messages.
    ///
    /// The handler receives messages and timer events asynchronously until
    /// [`unsubscribe`](Self::unsubscribe) is called. The handler reference is
    /// retained internally to prevent garbage collection on the C# side.
    ///
    /// # Errors
    ///
    /// Returns [`FfiError::Client`] if the consumer fails to start or
    /// topic subscription fails.
    pub async fn subscribe(&self, handler: Arc<dyn EventHandler>) -> Result<(), FfiError> {
        // Store the handler reference to keep it alive
        self.handler.store(Arc::new(Some(Arc::clone(&handler))));

        // Create the internal handler with propagator for distributed tracing
        let cs_handler = CsHandler::new(
            handler,
            Arc::new(new_propagator()),
            Arc::clone(&self.registry),
        );
        self.client.subscribe(cs_handler).await?;

        Ok(())
    }

    /// Stops consuming messages and unsubscribes from all topics.
    ///
    /// In-flight messages are allowed to complete before this method returns.
    /// The handler reference is released, allowing C# garbage collection.
    ///
    /// # Errors
    ///
    /// Returns [`FfiError::Client`] if the consumer fails to stop cleanly.
    pub async fn unsubscribe(&self) -> Result<(), FfiError> {
        // Unsubscribe from the client
        self.client.unsubscribe().await?;

        // Clear the handler reference
        self.handler.store(Arc::new(None));

        Ok(())
    }

    /// Shuts down the client and all its services.
    ///
    /// # Errors
    ///
    /// Returns [`FfiError::Client`] if shutdown fails.
    pub async fn shutdown(&self) -> Result<(), FfiError> {
        let result = self.client.clone().shutdown().await;
        self.handler.store(Arc::new(None));
        result?;
        Ok(())
    }

    /// Sends a message to a Kafka topic.
    ///
    /// The payload bytes are forwarded to Kafka verbatim; this method does
    /// not inspect or parse them. The caller supplies optional event metadata
    /// (typically pulled from the typed object on the C# side before JSON
    /// serialization), avoiding a JSON re-parse on the FFI boundary.
    /// `event_id` participates in producer idempotence dedup when present;
    /// `event_type` is carried for downstream consumers that filter on
    /// `allowed_events`. OpenTelemetry tracing context is extracted from the
    /// carrier to link the send operation with the parent span from C#.
    ///
    /// # Errors
    ///
    /// - [`FfiError::Client`] if the Kafka producer fails to deliver.
    pub async fn send(
        &self,
        topic: String,
        key: String,
        metadata: EventMetadata,
        payload: Vec<i8>,
        carrier: HashMap<String, String>,
    ) -> Result<(), FfiError> {
        // Extract OpenTelemetry context from carrier passed by C#
        let context = self.client.propagator().extract(&carrier);

        // Create span with extracted context as parent (matches C# SendAsync)
        let span = info_span!("csharp-Send", %topic, %key, aborted = Empty);
        if let Err(err) = span.set_parent(context) {
            debug!("failed to set parent span: {err:#}");
        }

        let binary_payload = BinaryPayload::new(
            bytemuck::cast_vec(payload),
            metadata.event_id,
            metadata.event_type,
        );

        let send_future = self
            .client
            .send(topic.as_str().into(), key, binary_payload)
            .instrument(span.clone());

        send_future.await?;
        span.record("aborted", false);

        Ok(())
    }

    /// Sends an excise record for a key.
    ///
    /// # Errors
    ///
    /// Returns an error when Prosody cannot send the record or the caller
    /// cancels the operation.
    pub async fn excise(
        &self,
        topic: String,
        key: String,
        carrier: HashMap<String, String>,
    ) -> Result<(), FfiError> {
        let context = self.client.propagator().extract(&carrier);
        let span = info_span!("csharp-Excise", %topic, %key, aborted = Empty);
        if let Err(err) = span.set_parent(context) {
            debug!("failed to set parent span: {err:#}");
        }
        let excise_future = self
            .client
            .excise(topic.as_str().into(), key)
            .instrument(span.clone());
        excise_future.await?;
        span.record("aborted", false);
        Ok(())
    }

    /// Sends one request and returns one outcome per subsystem.
    ///
    /// # Errors
    ///
    /// Returns an error for invalid arguments, a send failure, or shutdown.
    pub async fn request(
        &self,
        request: NativeRequest,
    ) -> Result<HashMap<String, NativeRequestResult>, FfiError> {
        let subsystems = request
            .subsystems
            .into_iter()
            .map(SubsystemName::try_new)
            .collect::<Result<Vec<_>, _>>()
            .map_err(|error| FfiError::PermanentState(error.to_string()))?;
        let context = self.client.propagator().extract(&request.carrier);
        let span = info_span!("csharp-request", topic = %request.topic, key = %request.key);
        if let Err(error) = span.set_parent(context) {
            debug!("failed to set parent span: {error:#}");
        }
        let payload = BinaryPayload::new(
            request.payload,
            request.metadata.event_id,
            request.metadata.event_type,
        );
        let request = self
            .client
            .request(
                Vec::new(),
                request.topic.as_str().into(),
                request.key,
                payload,
                subsystems,
                request.timeout,
            )
            .instrument(span);
        let results = request.await?;
        Ok(results
            .into_iter()
            .map(|(subsystem, result)| (subsystem.to_string(), native_request_result(result)))
            .collect())
    }

    /// Sends one excise request and returns one outcome per subsystem.
    ///
    /// # Errors
    ///
    /// Returns an error for invalid arguments, a send failure, or shutdown.
    pub async fn request_excise(
        &self,
        request: NativeExciseRequest,
    ) -> Result<HashMap<String, NativeRequestResult>, FfiError> {
        let subsystems = request
            .subsystems
            .into_iter()
            .map(SubsystemName::try_new)
            .collect::<Result<Vec<_>, _>>()
            .map_err(|error| FfiError::PermanentState(error.to_string()))?;
        let context = self.client.propagator().extract(&request.carrier);
        let span = info_span!("csharp-request-excise", topic = %request.topic, key = %request.key);
        if let Err(error) = span.set_parent(context) {
            debug!("failed to set parent span: {error:#}");
        }
        let request = self
            .client
            .request_excise(
                Vec::new(),
                request.topic.as_str().into(),
                request.key,
                subsystems,
                request.timeout,
            )
            .instrument(span);
        let results = request.await?;
        Ok(results
            .into_iter()
            .map(|(subsystem, result)| (subsystem.to_string(), native_request_result(result)))
            .collect())
    }

    /// Returns the current consumer state.
    pub async fn consumer_state(&self) -> ConsumerState {
        match self.client.consumer_state().await {
            ErasedConsumerState::Shutdown => ConsumerState::Shutdown,
            ErasedConsumerState::Unconfigured => ConsumerState::Unconfigured,
            ErasedConsumerState::ConfigurationFailed(error) => {
                ConsumerState::ConfigurationFailed { message: error }
            }
            ErasedConsumerState::Configured(_) => ConsumerState::Configured,
            ErasedConsumerState::Running { .. } => ConsumerState::Running,
        }
    }

    /// Returns the number of partitions currently assigned to this consumer.
    pub async fn assigned_partition_count(&self) -> u32 {
        self.client.assigned_partition_count().await
    }

    /// Returns `true` if the consumer is currently stalled.
    pub async fn is_stalled(&self) -> bool {
        self.client.is_stalled().await
    }

    /// Returns the source system identifier configured for this client.
    pub fn source_system(&self) -> String {
        self.client.source_system().to_owned()
    }
}
