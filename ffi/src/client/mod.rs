//! Low-level `UniFFI` client for the C# binding.

use std::collections::HashMap;
use std::sync::Arc;

use tracing::field::Empty;
use tracing::{Instrument, info_span};

use crate::cancellation::{CancellationSignal, cancellable};
use crate::config::{
    build_cassandra_config, build_consumer_builders, build_producer_config, get_mode,
};
use crate::error::FfiError;
use crate::handler::{
    CsHandler, EventHandler, NativeExciseRequest, NativeRequest, NativeRequestResult,
};
use crate::logging::ensure_tracing_initialized;
use crate::published::{
    PublishedDequeHandle, PublishedMapHandle, PublishedSetHandle, PublishedValueHandle,
    deque_handle, map_handle, set_handle, value_handle,
};
use crate::runtime::run;
use crate::state::with_parent;
use crate::types::{ClientOptions, ConsumerState, EventMetadata, ReadCache};
use prosody::codec::BinaryPayload;
use prosody::high_level::erased::{
    ErasedConsumerState, ErasedReadCache, SharedHighLevelClient, new_erased,
};
use prosody::propagator::new_propagator;

mod outcome;

use outcome::{native_request_results, subsystem_names};

/// Native Prosody client. The C# `Prosody.ProsodyClient` class wraps it and
/// documents the public API. The type is `Send + Sync`.
#[derive(uniffi::Object)]
pub struct ProsodyClient {
    /// Underlying prosody high-level client instance.
    client: SharedHighLevelClient<CsHandler>,
}

/// UniFFI-exported methods for [`ProsodyClient`].
#[uniffi::export]
impl ProsodyClient {
    /// Creates a client and initializes tracing once for the process.
    ///
    /// # Errors
    ///
    /// Returns [`FfiError::InvalidOperation`] if the options are invalid, and
    /// [`FfiError::Client`] if Kafka or Cassandra is not available.
    #[uniffi::constructor]
    pub async fn new(options: ClientOptions) -> Result<Self, FfiError> {
        run(async move {
            // Ensure tracing is initialized (idempotent)
            ensure_tracing_initialized();

            // Build all configuration from ClientOptions
            let mut producer_config = build_producer_config(&options);
            let consumer_builders = build_consumer_builders(&options)?;
            let cassandra = build_cassandra_config(&options);
            let mode = get_mode(&options);

            let client = Box::pin(new_erased(
                mode,
                &mut producer_config,
                &consumer_builders,
                &cassandra,
            ))
            .await?;

            Ok(Self { client })
        })
        .await
    }

    /// Opens a read-only published value collection.
    ///
    /// # Errors
    ///
    /// Returns [`FfiError::InvalidArgument`] for an empty subsystem name, and
    /// a state error with the category that Prosody gives the failure.
    pub async fn published_value(
        self: Arc<Self>,
        subsystem: String,
        name: String,
        cache: Option<ReadCache>,
    ) -> Result<Arc<PublishedValueHandle>, FfiError> {
        run(async move {
            let cache = cache.map_or(ErasedReadCache::Inherit, Into::into);
            let reader = self.client.value_state(subsystem, name, cache).await?;
            Ok(value_handle(reader))
        })
        .await
    }

    /// Opens a read-only published map collection.
    ///
    /// # Errors
    ///
    /// Returns [`FfiError::InvalidArgument`] for an empty subsystem name, and
    /// a state error with the category that Prosody gives the failure.
    pub async fn published_map(
        self: Arc<Self>,
        subsystem: String,
        name: String,
        cache: Option<ReadCache>,
    ) -> Result<Arc<PublishedMapHandle>, FfiError> {
        run(async move {
            let cache = cache.map_or(ErasedReadCache::Inherit, Into::into);
            let reader = self.client.map_state(subsystem, name, cache).await?;
            Ok(map_handle(reader))
        })
        .await
    }

    /// Opens a read-only published set collection.
    ///
    /// # Errors
    ///
    /// Returns [`FfiError::InvalidArgument`] for an empty subsystem name, and
    /// a state error with the category that Prosody gives the failure.
    pub async fn published_set(
        self: Arc<Self>,
        subsystem: String,
        name: String,
        cache: Option<ReadCache>,
    ) -> Result<Arc<PublishedSetHandle>, FfiError> {
        run(async move {
            let cache = cache.map_or(ErasedReadCache::Inherit, Into::into);
            let reader = self.client.set_state(subsystem, name, cache).await?;
            Ok(set_handle(reader))
        })
        .await
    }

    /// Opens a read-only published deque collection.
    ///
    /// # Errors
    ///
    /// Returns [`FfiError::InvalidArgument`] for an empty subsystem name, and
    /// a state error with the category that Prosody gives the failure.
    pub async fn published_deque(
        self: Arc<Self>,
        subsystem: String,
        name: String,
        cache: Option<ReadCache>,
    ) -> Result<Arc<PublishedDequeHandle>, FfiError> {
        run(async move {
            let cache = cache.map_or(ErasedReadCache::Inherit, Into::into);
            let reader = self.client.deque_state(subsystem, name, cache).await?;
            Ok(deque_handle(reader))
        })
        .await
    }

    /// Subscribes the handler. The consumer owns it until unsubscribe.
    ///
    /// # Errors
    ///
    /// Returns [`FfiError::InvalidOperation`] if the consumer is already
    /// subscribed, and [`FfiError::Client`] if the consumer fails to start.
    pub async fn subscribe(
        self: Arc<Self>,
        handler: Arc<dyn EventHandler>,
    ) -> Result<(), FfiError> {
        run(async move {
            // Wrap the handler with a trace propagator
            let cs_handler = CsHandler::new(handler, Arc::new(new_propagator()));
            self.client.subscribe(cs_handler).await?;

            Ok(())
        })
        .await
    }

    /// Stops the consumer after in-flight messages complete.
    ///
    /// # Errors
    ///
    /// Returns [`FfiError::InvalidOperation`] if the consumer is not
    /// subscribed, and [`FfiError::Client`] if it fails to stop cleanly.
    pub async fn unsubscribe(self: Arc<Self>) -> Result<(), FfiError> {
        run(async move {
            self.client.unsubscribe().await?;
            Ok(())
        })
        .await
    }

    /// Shuts down the client and all its services.
    ///
    /// # Errors
    ///
    /// Returns [`FfiError::Client`] if shutdown fails.
    pub async fn shutdown(self: Arc<Self>) -> Result<(), FfiError> {
        run(async move {
            self.client.clone().shutdown().await?;
            Ok(())
        })
        .await
    }

    /// Sends the payload bytes as they are, with the host event metadata.
    ///
    /// # Errors
    ///
    /// - [`FfiError::Cancelled`] if the cancellation signal was triggered.
    /// - [`FfiError::Client`] if the Kafka producer fails to deliver.
    pub async fn send(
        self: Arc<Self>,
        topic: String,
        key: String,
        metadata: EventMetadata,
        payload: Vec<u8>,
        carrier: HashMap<String, String>,
        cancel: Option<Arc<CancellationSignal>>,
    ) -> Result<(), FfiError> {
        run(async move {
            let span = info_span!("csharp-Send", %topic, %key, aborted = Empty);
            let span = with_parent(span, self.client.propagator(), &carrier);
            let payload = BinaryPayload::new(payload, metadata.event_id, metadata.event_type);
            let send = self
                .client
                .send(topic.as_str().into(), key, payload)
                .instrument(span.clone());

            let result = cancellable(cancel, send).await;
            span.record("aborted", matches!(result, Err(FfiError::Cancelled)));
            result
        })
        .await
    }

    /// Sends an excise record for a key.
    ///
    /// # Errors
    ///
    /// Returns an error when Prosody cannot send the record or the caller
    /// cancels the operation.
    pub async fn excise(
        self: Arc<Self>,
        topic: String,
        key: String,
        carrier: HashMap<String, String>,
        cancel: Option<Arc<CancellationSignal>>,
    ) -> Result<(), FfiError> {
        run(async move {
            let span = info_span!("csharp-Excise", %topic, %key, aborted = Empty);
            let span = with_parent(span, self.client.propagator(), &carrier);
            let excise = self
                .client
                .excise(topic.as_str().into(), key)
                .instrument(span.clone());

            let result = cancellable(cancel, excise).await;
            span.record("aborted", matches!(result, Err(FfiError::Cancelled)));
            result
        })
        .await
    }

    /// Sends one request and returns one outcome per subsystem.
    ///
    /// # Errors
    ///
    /// Returns an error for invalid arguments, a send failure, or shutdown.
    pub async fn request(
        self: Arc<Self>,
        request: NativeRequest,
        cancel: Option<Arc<CancellationSignal>>,
    ) -> Result<HashMap<String, NativeRequestResult>, FfiError> {
        run(async move {
            let subsystems = subsystem_names(request.subsystems)?;
            let span = info_span!("csharp-request", topic = %request.topic, key = %request.key);
            let span = with_parent(span, self.client.propagator(), &request.carrier);
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

            cancellable(cancel, request)
                .await
                .map(native_request_results)
        })
        .await
    }

    /// Sends one excise request and returns one outcome per subsystem.
    ///
    /// # Errors
    ///
    /// Returns an error for invalid arguments, a send failure, or shutdown.
    pub async fn request_excise(
        self: Arc<Self>,
        request: NativeExciseRequest,
        cancel: Option<Arc<CancellationSignal>>,
    ) -> Result<HashMap<String, NativeRequestResult>, FfiError> {
        run(async move {
            let subsystems = subsystem_names(request.subsystems)?;
            let span =
                info_span!("csharp-request-excise", topic = %request.topic, key = %request.key);
            let span = with_parent(span, self.client.propagator(), &request.carrier);
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

            cancellable(cancel, request)
                .await
                .map(native_request_results)
        })
        .await
    }

    /// Returns the current consumer state.
    pub async fn consumer_state(self: Arc<Self>) -> ConsumerState {
        run(async move {
            match self.client.consumer_state().await {
                ErasedConsumerState::Shutdown => ConsumerState::Shutdown,
                ErasedConsumerState::Unconfigured => ConsumerState::Unconfigured,
                ErasedConsumerState::ConfigurationFailed(error) => {
                    ConsumerState::ConfigurationFailed { message: error }
                }
                ErasedConsumerState::Configured(_) => ConsumerState::Configured,
                ErasedConsumerState::Running { .. } => ConsumerState::Running,
            }
        })
        .await
    }

    /// Returns the number of partitions currently assigned to this consumer.
    pub async fn assigned_partition_count(self: Arc<Self>) -> u32 {
        run(async move { self.client.assigned_partition_count().await }).await
    }

    /// Returns `true` if the consumer is currently stalled.
    pub async fn is_stalled(self: Arc<Self>) -> bool {
        run(async move { self.client.is_stalled().await }).await
    }

    /// Returns the source system identifier configured for this client.
    #[must_use]
    pub fn source_system(&self) -> String {
        self.client.source_system().to_owned()
    }
}
