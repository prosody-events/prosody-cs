# Prosody C#

> **Work in Progress** - This library is under active development and not yet ready for production use.

C# bindings for the [Prosody](https://github.com/cincpro/prosody) Kafka client library.

## Project Structure

```
prosody-cs/
├── native/                      # Rust FFI crate
│   ├── Cargo.toml
│   ├── build.rs                 # csbindgen code generation
│   └── src/
│       ├── lib.rs               # Module root
│       ├── runtime.rs           # Tokio runtime management
│       ├── client.rs            # HighLevelClient FFI
│       ├── handler.rs           # Handler callback bridge
│       ├── context.rs           # EventContext FFI
│       └── types.rs             # FFI primitives
│
├── src/
│   └── Prosody/                 # Main C# library
│       ├── Prosody.csproj
│       ├── ProsodyClient.cs     # High-level client
│       ├── ProsodyClientOptions.cs
│       ├── IEventHandler.cs     # Handler interface
│       ├── IEventContext.cs     # Context interface
│       ├── Message.cs           # Message type
│       ├── Trigger.cs           # Timer trigger type
│       ├── Exceptions.cs        # Exception types
│       └── Native/              # FFI internals
│           ├── NativeMethods.g.cs  # Generated P/Invoke
│           ├── NativeRuntime.cs
│           └── SafeHandles.cs
│
├── test/
│   └── Prosody.Tests/           # Unit tests
│
└── prosody-cs.sln
```

## Building

### Prerequisites

- .NET 8.0+ SDK (supports net8.0, net9.0, net10.0)
- Rust toolchain (for building native library)

### Build Native Library

```bash
cd native
cargo build --release
```

### Build C# Library

```bash
dotnet build
```

### Run Tests

```bash
dotnet test
```

## Usage

```csharp
using Prosody;

var options = new ProsodyClientOptions
{
    BootstrapServers = "localhost:9092",
    GroupId = "my-consumer-group",
    SubscribedTopics = ["my-topic"]
};

await using var client = new ProsodyClient(options);

// Subscribe with a handler
await client.SubscribeAsync(new MyEventHandler());

// Send messages
await client.SendAsync("my-topic", "key", new { Data = "value" });
```

### Implementing a Handler

```csharp
public class MyEventHandler : IEventHandler
{
    public async Task OnMessageAsync(IEventContext context, Message message, CancellationToken ct)
    {
        var payload = message.GetPayload<MyPayload>();
        // Process the message...

        // Schedule a timer for later processing
        await context.ScheduleAsync(DateTimeOffset.UtcNow.AddMinutes(5));
    }

    public async Task OnTimerAsync(IEventContext context, Trigger trigger, CancellationToken ct)
    {
        // Handle timer...
    }

    public bool IsPermanentError(Exception ex) => ex is PermanentException;
}
```

## License

UNLICENSED
