//! Tests that exports and core tasks run on the one runtime.
//!
//! Each test polls from a plain thread with no Tokio context, as `UniFFI`
//! does from a C# thread.

use std::collections::HashMap;
use std::process;
use std::sync::Arc;
use std::thread;
use std::time::Duration;

use async_trait::async_trait;
use color_eyre::Result;
use color_eyre::eyre::eyre;
use futures::executor::block_on;
use tokio::sync::mpsc::{UnboundedSender, unbounded_channel};
use tokio::time::timeout;

use super::{WORKER_NAME, run};
use crate::client::ProsodyClient;
use crate::context::Context;
use crate::error::FfiError;
use crate::handler::{EventHandler, HandlerResult};
use crate::message::{ExciseMessage, Message};
use crate::timer::Timer;
use crate::types::{ClientOptions, EventMetadata};

/// Records the name of the thread that calls each handler method.
struct ThreadRecorder {
    names: UnboundedSender<Option<String>>,
}

#[async_trait]
impl EventHandler for ThreadRecorder {
    async fn on_message(
        &self,
        _context: Arc<Context>,
        _message: Arc<Message>,
        _carrier: HashMap<String, String>,
    ) -> Result<HandlerResult, FfiError> {
        drop(self.names.send(current_name()));
        Ok(HandlerResult::default())
    }

    async fn on_excise(
        &self,
        _context: Arc<Context>,
        _message: Arc<ExciseMessage>,
        _carrier: HashMap<String, String>,
    ) -> Result<HandlerResult, FfiError> {
        drop(self.names.send(current_name()));
        Ok(HandlerResult::default())
    }

    async fn on_timer(
        &self,
        _context: Arc<Context>,
        _timer: Arc<Timer>,
        _carrier: HashMap<String, String>,
    ) -> Result<HandlerResult, FfiError> {
        drop(self.names.send(current_name()));
        Ok(HandlerResult::default())
    }
}

#[test]
fn run_and_its_spawns_use_workers() -> Result<()> {
    let (poller, spawned) = block_on(run(async {
        let spawned = tokio::spawn(async { current_name() }).await;
        (current_name(), spawned)
    }));

    assert_eq!(poller.as_deref(), Some(WORKER_NAME), "run body thread");
    assert_eq!(
        spawned?.as_deref(),
        Some(WORKER_NAME),
        "spawned task thread"
    );
    Ok(())
}

#[test]
fn handler_runs_on_workers() -> Result<()> {
    let topic = format!("runtime-test-{}", process::id());
    let options = ClientOptions {
        mock: Some(true),
        bootstrap_servers: Some(vec!["localhost:9092".to_owned()]),
        group_id: Some(topic.clone()),
        source_system: Some("runtime-test".to_owned()),
        subscribed_topics: Some(vec![topic.clone()]),
        ..ClientOptions::default()
    };
    let (names, mut received) = unbounded_channel();

    let name = block_on(async {
        let client = Arc::new(ProsodyClient::new(options).await?);
        Arc::clone(&client)
            .subscribe(Arc::new(ThreadRecorder { names }))
            .await?;
        Arc::clone(&client)
            .send(
                topic,
                "key".to_owned(),
                EventMetadata::default(),
                b"{}".to_vec(),
                HashMap::new(),
                None,
            )
            .await?;

        let name = run(async move { timeout(Duration::from_secs(60), received.recv()).await })
            .await?
            .ok_or_else(|| eyre!("handler channel closed"))?;

        client.shutdown().await?;
        Ok::<_, color_eyre::Report>(name)
    })?;

    assert_eq!(name.as_deref(), Some(WORKER_NAME), "handler thread");
    Ok(())
}

fn current_name() -> Option<String> {
    thread::current().name().map(str::to_owned)
}
