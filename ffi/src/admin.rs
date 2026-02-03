//! Admin client for Kafka topic management.
//!
//! Provides FFI bindings for administrative operations on Kafka topics,
//! such as creating and deleting topics for testing.

use std::sync::Arc;

use crate::error::FfiError;
use prosody::admin::{AdminConfiguration, ProsodyAdminClient, TopicConfiguration};
use tokio::spawn;

/// A client for performing administrative operations on Kafka topics.
///
/// Used primarily for integration testing to create and delete test topics.
#[derive(uniffi::Object)]
pub struct AdminClient {
    client: Arc<ProsodyAdminClient>,
}

#[uniffi::export(async_runtime = "tokio")]
impl AdminClient {
    /// Creates a new `AdminClient` with the specified bootstrap servers.
    ///
    /// # Arguments
    ///
    /// * `bootstrap_servers` - Kafka bootstrap servers to connect to.
    ///
    /// # Errors
    ///
    /// Returns a `FfiError` if the client cannot be created.
    #[uniffi::constructor]
    pub fn new(bootstrap_servers: Vec<String>) -> Result<Self, FfiError> {
        let config = AdminConfiguration::new(bootstrap_servers)?;
        let client = ProsodyAdminClient::new(&config)?;

        Ok(Self {
            client: Arc::new(client),
        })
    }

    /// Creates a new Kafka topic.
    ///
    /// # Arguments
    ///
    /// * `name` - The name of the topic to create.
    /// * `partition_count` - Number of partitions for the topic.
    /// * `replication_factor` - Replication factor for the topic.
    ///
    /// # Errors
    ///
    /// Returns a `FfiError` if topic creation fails.
    pub async fn create_topic(
        &self,
        name: String,
        partition_count: u16,
        replication_factor: u16,
    ) -> Result<(), FfiError> {
        let client = self.client.clone();

        spawn(async move {
            let config = TopicConfiguration::builder()
                .name(name)
                .partition_count(partition_count)
                .replication_factor(replication_factor)
                .build()?;

            client.create_topic(&config).await?;

            Ok::<(), FfiError>(())
        })
        .await?
    }

    /// Deletes a Kafka topic.
    ///
    /// # Arguments
    ///
    /// * `name` - The name of the topic to delete.
    ///
    /// # Errors
    ///
    /// Returns a `FfiError` if topic deletion fails.
    pub async fn delete_topic(&self, name: String) -> Result<(), FfiError> {
        let client = self.client.clone();

        spawn(async move {
            client.delete_topic(&name).await?;

            Ok::<(), FfiError>(())
        })
        .await?
    }
}
