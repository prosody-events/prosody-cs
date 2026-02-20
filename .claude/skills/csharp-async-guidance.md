---
name: csharp-async-guidance
description: Write correct async/await C# code. Covers viral async, async void pitfalls, TaskCompletionSource, CancellationToken flow, sync-over-async dangers, timer callbacks, AsyncLocal<T> best practices, and ConfigureAwait guidance.
invocable: false
---

> Source: [David Fowler - Async Guidance](https://github.com/davidfowl/AspNetCoreDiagnosticScenarios/blob/master/AsyncGuidance.md)

# Table of contents
 - [Asynchronous Programming](#asynchronous-programming)
   - [Asynchrony is viral](#asynchrony-is-viral)
   - [Async void](#async-void)
   - [Prefer Task.FromResult over Task.Run for pre-computed or trivially computed data](#prefer-taskfromresult-over-taskrun-for-pre-computed-or-trivially-computed-data)
   - [Avoid using Task.Run for long-running work that blocks the thread](#avoid-using-taskrun-for-long-running-work-that-blocks-the-thread)
   - [Avoid using Task.Result and Task.Wait](#avoid-using-taskresult-and-taskwait)
   - [Prefer await over ContinueWith](#prefer-await-over-continuewith)
   - [Always create TaskCompletionSource\<T\> with TaskCreationOptions.RunContinuationsAsynchronously](#always-create-taskcompletionsourcet-with-taskcreationoptionsruncontinuationsasynchronously)
   - [Always dispose CancellationTokenSource(s) used for timeouts](#always-dispose-cancellationtokensources-used-for-timeouts)
   - [Always flow CancellationToken(s) to APIs that take a CancellationToken](#always-flow-cancellationtokens-to-apis-that-take-a-cancellationtoken)
   - [Cancelling uncancellable operations](#cancelling-uncancellable-operations)
   - [Always call FlushAsync on StreamWriter(s) or Stream(s) before calling Dispose](#always-call-flushasync-on-streamwriters-or-streams-before-calling-dispose)
   - [Prefer async/await over directly returning Task](#prefer-asyncawait-over-directly-returning-task)
   - [AsyncLocal\<T\>](#asynclocalt)
   - [ConfigureAwait](#configureawait)
   - [Scenarios](#scenarios)
   - [Timer callbacks](#timer-callbacks)
   - [Implicit async void delegates](#implicit-async-void-delegates)
   - [ConcurrentDictionary.GetOrAdd](#concurrentdictionarygetoradd)
   - [Constructors](#constructors)

# Asynchronous Programming

Asynchronous programming has been around for several years on the .NET platform but has historically been very difficult to do well. Since the introduction of async/await
in C# 5 asynchronous programming has become mainstream. Modern frameworks (like ASP.NET Core) are fully asynchronous and it's very hard to avoid the async keyword when writing
web services. As a result, there's been lots of confusion on the best practices for async and how to use it properly. This section will try to lay out some guidance with examples of bad and good patterns of how to write asynchronous code.

## Asynchrony is viral

Once you go async, all of your callers **SHOULD** be async, since efforts to be async amount to nothing unless the entire call stack is async. In many cases, being partially asynchronous can be worse than being entirely synchronous. Therefore it is best to go all in, and make everything async at once.

❌ **BAD** This example uses the `Task.Result` and as a result blocks the current thread to wait for the result. This is an example of [sync over async](#avoid-using-taskresult-and-taskwait).

```C#
public int DoSomethingAsync()
{
    var result = CallDependencyAsync().Result;
    return result + 1;
}
```

✅ **GOOD** This example uses the await keyword to get the result from `CallDependencyAsync`.

```C#
public async Task<int> DoSomethingAsync()
{
    var result = await CallDependencyAsync();
    return result + 1;
}
```

## Async void

The use of async void in ASP.NET Core applications is **ALWAYS** bad. Avoid it, never do it. Typically, it's used when developers are trying to implement fire-and-forget patterns triggered by a controller action. Async void methods will crash the process if an exception is thrown. We'll look at more of the patterns that cause developers to do this in ASP.NET Core applications but here's a simple example:

❌ **BAD** Async void methods can't be tracked and therefore unhandled exceptions can result in application crashes.

```C#
public class MyController : Controller
{
    [HttpPost("/start")]
    public IActionResult Post()
    {
        BackgroundOperationAsync();
        return Accepted();
    }

    public async void BackgroundOperationAsync()
    {
        var result = await CallDependencyAsync();
        DoSomething(result);
    }
}
```

✅ **GOOD** `Task`-returning methods are better since unhandled exceptions trigger the `TaskScheduler.UnobservedTaskException`.

```C#
public class MyController : Controller
{
    [HttpPost("/start")]
    public IActionResult Post()
    {
        Task.Run(BackgroundOperationAsync);
        return Accepted();
    }

    public async Task BackgroundOperationAsync()
    {
        var result = await CallDependencyAsync();
        DoSomething(result);
    }
}
```

## Prefer `Task.FromResult` over `Task.Run` for pre-computed or trivially computed data

For pre-computed results, there's no need to call `Task.Run`, which will end up queuing a work item to the thread pool that will immediately complete with the pre-computed value. Instead, use `Task.FromResult`, to create a task wrapping already computed data.

❌ **BAD** This example wastes a thread-pool thread to return a trivially computed value.

```C#
public class MyLibrary
{
   public Task<int> AddAsync(int a, int b)
   {
       return Task.Run(() => a + b);
   }
}
```

✅ **GOOD** This example uses `Task.FromResult` to return the trivially computed value. It does not use any extra threads as a result.

```C#
public class MyLibrary
{
   public Task<int> AddAsync(int a, int b)
   {
       return Task.FromResult(a + b);
   }
}
```

NOTE: Using `Task.FromResult` will result in a `Task` allocation. Using `ValueTask<T>` can completely remove that allocation.

✅ **GOOD** This example uses a `ValueTask<int>` to return the trivially computed value. It does not use any extra threads as a result. It also does not allocate an object on the managed heap.

```C#
public class MyLibrary
{
   public ValueTask<int> AddAsync(int a, int b)
   {
       return new ValueTask<int>(a + b);
   }
}
```

## Avoid using Task.Run for long-running work that blocks the thread

Long-running work in this context refers to a thread that's running for the lifetime of the application doing background work (like processing queue items, or sleeping and waking up to process some data). `Task.Run` will queue a work item to the thread pool. The assumption is that that work will finish quickly (or quickly enough to allow reusing that thread within some reasonable timeframe). Stealing a thread-pool thread for long-running work is bad since it takes that thread away from other work that could be done (timer callbacks, task continuations, etc). Instead, spawn a new thread manually to do long-running blocking work.

NOTE: The thread pool grows if you block threads but it's bad practice to do so.

NOTE: `Task.Factory.StartNew` has an option `TaskCreationOptions.LongRunning` that under the covers creates a new thread and returns a Task that represents the execution. Using this properly requires several non-obvious parameters to be passed in to get the right behavior on all platforms.

NOTE: Don't use `TaskCreationOptions.LongRunning` with async code as this will create a new thread which will be destroyed after first `await`.


❌ **BAD** This example steals a thread-pool thread forever, to execute queued work on a `BlockingCollection<T>`.

```C#
public class QueueProcessor
{
    private readonly BlockingCollection<Message> _messageQueue = new BlockingCollection<Message>();

    public void StartProcessing()
    {
        Task.Run(ProcessQueue);
    }

    public void Enqueue(Message message)
    {
        _messageQueue.Add(message);
    }

    private void ProcessQueue()
    {
        foreach (var item in _messageQueue.GetConsumingEnumerable())
        {
             ProcessItem(item);
        }
    }

    private void ProcessItem(Message message) { }
}
```

✅ **GOOD** This example uses a dedicated thread to process the message queue instead of a thread-pool thread.

```C#
public class QueueProcessor
{
    private readonly BlockingCollection<Message> _messageQueue = new BlockingCollection<Message>();

    public void StartProcessing()
    {
        var thread = new Thread(ProcessQueue)
        {
            // This is important as it allows the process to exit while this thread is running
            IsBackground = true
        };
        thread.Start();
    }

    public void Enqueue(Message message)
    {
        _messageQueue.Add(message);
    }

    private void ProcessQueue()
    {
        foreach (var item in _messageQueue.GetConsumingEnumerable())
        {
             ProcessItem(item);
        }
    }

    private void ProcessItem(Message message) { }
}
```

✅ **GOOD** This example utilizes a `TaskFactory` with `TaskCreationOptions.LongRunning` to process the message queue instead of creating a thread manually.

```C#
public class QueueProcessor
{
    private readonly BlockingCollection<Message> _messageQueue = new BlockingCollection<Message>();

    public Task StartProcessing() => Task.Factory.StartNew(ProcessQueue, TaskCreationOptions.LongRunning);

    public void Enqueue(Message message)
    {
        _messageQueue.Add(message);
    }

    private void ProcessQueue()
    {
        foreach (var item in _messageQueue.GetConsumingEnumerable())
        {
            ProcessItem(item);
        }
    }

    private void ProcessItem(Message message) { }
}
```

## Avoid using `Task.Result` and `Task.Wait`

There are very few ways to use `Task.Result` and `Task.Wait` correctly so the general advice is to completely avoid using them in your code.

### Sync over async

Using `Task.Result` or `Task.Wait` to block waiting on an asynchronous operation to complete is *MUCH* worse than calling a truly synchronous API to block. This phenomenon is dubbed "Sync over async". Here is what happens at a very high level:

- An asynchronous operation is kicked off.
- The calling thread is blocked waiting for that operation to complete.
- When the asynchronous operation completes, it unblocks the code waiting on that operation. This takes place on another thread.

The result is that we need to use 2 threads instead of 1 to complete synchronous operations. This usually leads to thread-pool starvation and results in service outages.

### Deadlocks

The `SynchronizationContext` is an abstraction that gives application models a chance to control where asynchronous continuations run. ASP.NET (non-core), WPF, and Windows Forms each have an implementation that will result in a deadlock if Task.Wait or Task.Result is used on the main thread.

NOTE: ASP.NET Core does not have a `SynchronizationContext` and is not prone to the deadlock problem.

❌ **BAD** The below are all examples that are, in one way or another, trying to avoid the deadlock situation but still succumb to "sync over async" problems.

```C#
public string DoOperationBlocking()
{
    return Task.Run(() => DoAsyncOperation()).Result;
}

public string DoOperationBlocking2()
{
    return Task.Run(() => DoAsyncOperation()).GetAwaiter().GetResult();
}

public string DoOperationBlocking5()
{
    return DoAsyncOperation().Result;
}

public string DoOperationBlocking6()
{
    return DoAsyncOperation().GetAwaiter().GetResult();
}

public string DoOperationBlocking7()
{
    var task = DoAsyncOperation();
    task.Wait();
    return task.GetAwaiter().GetResult();
}
```

## Prefer `await` over `ContinueWith`

`Task` existed before the async/await keywords were introduced and as such provided ways to execute continuations without relying on the language. Although these methods are still valid to use, we generally recommend that you prefer `async`/`await` to using `ContinueWith`. `ContinueWith` also does not capture the `SynchronizationContext` and as a result is actually semantically different to `async`/`await`.

❌ **BAD** The example uses `ContinueWith` instead of `async`

```C#
public Task<int> DoSomethingAsync()
{
    return CallDependencyAsync().ContinueWith(task =>
    {
        return task.Result + 1;
    });
}
```

✅ **GOOD** This example uses the `await` keyword to get the result from `CallDependencyAsync`.

```C#
public async Task<int> DoSomethingAsync()
{
    var result = await CallDependencyAsync();
    return result + 1;
}
```

## Always create `TaskCompletionSource<T>` with `TaskCreationOptions.RunContinuationsAsynchronously`

`TaskCompletionSource<T>` is an important building block for libraries trying to adapt things that are not inherently awaitable to be awaitable via a `Task`. It is also commonly used to build higher-level operations (such as batching and other combinators) on top of existing asynchronous APIs. By default, `Task` continuations will run *inline* on the same thread that calls Try/Set(Result/Exception/Canceled). As a library author, this means having to understand that calling code can resume directly on your thread. This is extremely dangerous and can result in deadlocks, thread-pool starvation, corruption of state (if code runs unexpectedly) and more.

Always use `TaskCreationOptions.RunContinuationsAsynchronously` when creating the `TaskCompletionSource<T>`. This will dispatch the continuation onto the thread pool instead of executing it inline.

❌ **BAD** This example does not use `TaskCreationOptions.RunContinuationsAsynchronously` when creating the `TaskCompletionSource<T>`.

```C#
public Task<int> DoSomethingAsync()
{
    var tcs = new TaskCompletionSource<int>();

    var operation = new LegacyAsyncOperation();
    operation.Completed += result =>
    {
        // Code awaiting on this task will resume on this thread!
        tcs.SetResult(result);
    };

    return tcs.Task;
}
```

✅ **GOOD** This example uses `TaskCreationOptions.RunContinuationsAsynchronously` when creating the `TaskCompletionSource<T>`.

```C#
public Task<int> DoSomethingAsync()
{
    var tcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);

    var operation = new LegacyAsyncOperation();
    operation.Completed += result =>
    {
        // Code awaiting on this task will resume on a different thread-pool thread
        tcs.SetResult(result);
    };

    return tcs.Task;
}
```

NOTE: There are 2 enums that look alike. `TaskCreationOptions.RunContinuationsAsynchronously` and `TaskContinuationOptions.RunContinuationsAsynchronously`. Be careful not to confuse their usage.

## Always dispose `CancellationTokenSource`(s) used for timeouts

`CancellationTokenSource` objects that are used for timeouts (are created with timers or use the `CancelAfter` method), can put pressure on the timer queue if not disposed.

❌ **BAD** This example does not dispose of the `CancellationTokenSource` and as a result, the timer stays in the queue for 10 seconds after each request is made.

```C#
public async Task<Stream> HttpClientAsyncWithCancellationBad()
{
    var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

    using (var client = _httpClientFactory.CreateClient())
    {
        var response = await client.GetAsync("http://backend/api/1", cts.Token);
        return await response.Content.ReadAsStreamAsync();
    }
}
```

✅ **GOOD** This example disposes of the `CancellationTokenSource` and properly removes the timer from the queue.

```C#
public async Task<Stream> HttpClientAsyncWithCancellationGood()
{
    using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
    {
        using (var client = _httpClientFactory.CreateClient())
        {
            var response = await client.GetAsync("http://backend/api/1", cts.Token);
            return await response.Content.ReadAsStreamAsync();
        }
    }
}
```

## Always flow `CancellationToken`(s) to APIs that take a `CancellationToken`

Cancellation is cooperative in .NET. Everything in the call chain has to be explicitly passed the `CancellationToken` in order for it to work well. This means you need to explicitly pass the token into other APIs that take a token if you want cancellation to be most effective.

❌ **BAD** This example neglects to pass the `CancellationToken` to `Stream.ReadAsync` making the operation effectively not cancellable.

```C#
public async Task<string> DoAsyncThing(CancellationToken cancellationToken = default)
{
   byte[] buffer = new byte[1024];
   // We forgot to pass flow cancellationToken to ReadAsync
   int read = await _stream.ReadAsync(buffer, 0, buffer.Length);
   return Encoding.UTF8.GetString(buffer, 0, read);
}
```

✅ **GOOD** This example passes the `CancellationToken` into `Stream.ReadAsync`.

```C#
public async Task<string> DoAsyncThing(CancellationToken cancellationToken = default)
{
   byte[] buffer = new byte[1024];
   // This properly flows cancellationToken to ReadAsync
   int read = await _stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken);
   return Encoding.UTF8.GetString(buffer, 0, read);
}
```

## Cancelling uncancellable operations

### Using CancellationTokens

✅ **GOOD** Dispose of the `CancellationTokenRegistration` when one of the `Task(s)` is complete.

```C#
public static async Task<T> WithCancellation<T>(this Task<T> task, CancellationToken cancellationToken)
{
    var tcs = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);

    using (cancellationToken.Register(state =>
    {
        ((TaskCompletionSource<object>)state).TrySetResult(null);
    },
    tcs))
    {
        var resultTask = await Task.WhenAny(task, tcs.Task);
        if (resultTask == tcs.Task)
        {
            throw new OperationCanceledException(cancellationToken);
        }

        return await task;
    }
}
```

✅ **GOOD** Prefer `Task.WaitAsync` on .NET >= 6.

### Using a timeout

✅ **GOOD** Cancel the timer if the operation successfully completes.

```C#
public static async Task<T> TimeoutAfter<T>(this Task<T> task, TimeSpan timeout)
{
    using (var cts = new CancellationTokenSource())
    {
        var delayTask = Task.Delay(timeout, cts.Token);

        var resultTask = await Task.WhenAny(task, delayTask);
        if (resultTask == delayTask)
        {
            throw new OperationCanceledException();
        }
        else
        {
            cts.Cancel();
        }

        return await task;
    }
}
```

✅ **GOOD** Prefer `Task.WaitAsync` on .NET >= 6.

## Always call `FlushAsync` on `StreamWriter`(s) or `Stream`(s) before calling `Dispose`

When writing to a `Stream` or `StreamWriter`, even if the asynchronous overloads are used for writing, the underlying data might be buffered. When data is buffered, disposing the `Stream` or `StreamWriter` via the `Dispose` method will synchronously write/flush, which results in blocking the thread. Either use the asynchronous `DisposeAsync` method (via `await using`) or call `FlushAsync` before calling `Dispose`.

✅ **GOOD** Use `await using` for asynchronous dispose:

```C#
app.Run(async context =>
{
    await using (var streamWriter = new StreamWriter(context.Response.Body))
    {
        await streamWriter.WriteAsync("Hello World");
    }
});
```

## Prefer `async`/`await` over directly returning `Task`

Benefits of using `async`/`await` instead of directly returning the `Task`:
- Asynchronous and synchronous exceptions are normalized to always be asynchronous.
- The code is easier to modify (consider adding a `using`, for example).
- Diagnostics of asynchronous methods are easier (debugging hangs etc).
- Exceptions thrown will be automatically wrapped in the returned `Task` instead of surprising the caller with an actual exception.
- Async locals will not leak out of async methods.

❌ **BAD** This example directly returns the `Task` to the caller.

```C#
public Task<int> DoSomethingAsync()
{
    return CallDependencyAsync();
}
```

✅ **GOOD** This example uses async/await instead of directly returning the Task.

```C#
public async Task<int> DoSomethingAsync()
{
    return await CallDependencyAsync();
}
```

NOTE: There are performance considerations when using an async state machine over directly returning the `Task`. It's always faster to directly return the `Task` since it does less work but you end up changing the behavior and potentially losing some of the benefits of the async state machine.

## AsyncLocal\<T\>

Async locals are a way to store/retrieve ambient state throughout an application. If you can avoid async locals, do so by explicitly passing state around or using techniques like inversion of control.

If you cannot avoid it, make sure that anything put into an async local is:

1. Not disposable
2. Immutable/read-only/thread-safe

Key rules:
- Don't store disposable objects in async locals (race conditions with disposal)
- Don't store non-thread-safe objects in async locals (concurrent access from multiple threads)
- Use `CancellationToken.UnsafeRegister` instead of `CancellationToken.Register` when you don't need execution context flow (avoids memory leaks from captured async locals)
- Set async locals in async methods (not sync methods) to avoid value propagation leaking outside the method
- The execution context is copy-on-write: setting an async local to null creates a new context, it does not mutate previously captured contexts

## Timer callbacks

❌ **BAD** Using `async void` in timer callbacks will crash the process on exceptions.

✅ **GOOD** Use an `async Task`-based method and discard the `Task` in the `Timer` callback:

```C#
public class Pinger
{
    private readonly Timer _timer;
    private readonly HttpClient _client;

    public Pinger(HttpClient client)
    {
        _client = client;
        _timer = new Timer(Heartbeat, null, 1000, 1000);
    }

    public void Heartbeat(object state)
    {
        _ = DoAsyncPing();
    }

    private async Task DoAsyncPing()
    {
        await _client.GetAsync("http://mybackend/api/ping");
    }
}
```

✅ **GOOD** Use `PeriodicTimer` (.NET 6+):

```C#
public class Pinger : IDisposable
{
    private readonly PeriodicTimer _timer;
    private readonly HttpClient _client;

    public Pinger(HttpClient client)
    {
        _client = client;
        _timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        _ = Task.Run(DoAsyncPings);
    }

    public void Dispose()
    {
        _timer.Dispose();
    }

    private async Task DoAsyncPings()
    {
        while (await _timer.WaitForNextTickAsync())
        {
            await _client.GetAsync("http://mybackend/api/ping");
        }
    }
}
```

## Implicit `async void` delegates

❌ **BAD** APIs that accept only `Action` callbacks force callers into `async void` implicitly.

✅ **GOOD** Offer both sync and `async` callback overloads:

```C#
public class BackgroundQueue
{
    public static void FireAndForget(Action action) { }
    public static void FireAndForget(Func<Task> action) { }
}
```

## Constructors

Constructors are synchronous. If you need to initialize some logic that may be asynchronous, use a static factory pattern:

✅ **GOOD** Static factory pattern for asynchronous construction:

```C#
public class Service : IService
{
    private readonly IRemoteConnection _connection;

    private Service(IRemoteConnection connection)
    {
        _connection = connection;
    }

    public static async Task<Service> CreateAsync(IRemoteConnectionFactory connectionFactory)
    {
        return new Service(await connectionFactory.ConnectAsync());
    }
}
```
