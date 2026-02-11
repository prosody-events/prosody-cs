//! FFI bindings for Kafka administrative operations.
//!
//! This module exposes [`AdminClient`], which provides async methods for
//! managing Kafka topics (create, delete). All operations are asynchronous
//! and run on the Tokio runtime.

use std::sync::Arc;

use crate::error::FfiError;
use prosody::admin::{AdminConfiguration, ProsodyAdminClient, TopicConfiguration};

/// Async client for Kafka topic administration.
///
/// Wraps the Prosody admin client for FFI exposure. Primarily used in
/// integration tests to set up and tear down test topics.
#[derive(uniffi::Object)]
pub struct AdminClient {
    client: Arc<ProsodyAdminClient>,
}

#[uniffi::export(async_runtime = "tokio")]
impl AdminClient {
    /// Creates a new admin client connected to the given brokers.
    ///
    /// # Errors
    ///
    /// Returns [`FfiError`] if configuration is invalid or the underlying
    /// client fails to initialize.
    #[uniffi::constructor]
    pub fn new(bootstrap_servers: Vec<String>) -> Result<Self, FfiError> {
        let config = AdminConfiguration::new(bootstrap_servers)?;
        let client = ProsodyAdminClient::new(&config)?;

        Ok(Self {
            client: Arc::new(client),
        })
    }

    /// Creates a Kafka topic with the specified configuration.
    ///
    /// # Errors
    ///
    /// Returns [`FfiError`] if the topic configuration is invalid or
    /// the broker rejects the creation request (e.g., topic already exists,
    /// insufficient replication factor).
    pub async fn create_topic(
        &self,
        name: String,
        partition_count: u16,
        replication_factor: u16,
    ) -> Result<(), FfiError> {
        let config = TopicConfiguration::builder()
            .name(name)
            .partition_count(partition_count)
            .replication_factor(replication_factor)
            .build()?;

        self.client.create_topic(&config).await?;

        Ok(())
    }

    /// Deletes a Kafka topic by name.
    ///
    /// # Errors
    ///
    /// Returns [`FfiError`] if the topic does not exist or the broker
    /// rejects the deletion request.
    pub async fn delete_topic(&self, name: String) -> Result<(), FfiError> {
        self.client.delete_topic(&name).await?;

        Ok(())
    }
}
