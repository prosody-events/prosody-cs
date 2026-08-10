using Prosody.Messaging;
using Prosody.State;
using Native = Prosody.Native;

namespace Prosody.Tests.Unit;

/// <summary>
/// Pins the normative C# keyed-state example from the plan: it must compile against the public
/// surface, the definitions must construct, and the builder chain must accept them.
/// </summary>
public sealed class NormativeExampleCompileTests
{
    private static readonly ValueStateDefinition<Cart> CartDef = StateDefinition.Value<Cart>(
        "cart",
        ttl: TimeSpan.FromDays(30)
    );
    private static readonly MapStateDefinition<decimal> TotalsDef = StateDefinition.Map<decimal>("totals");
    private static readonly MessageDequeDefinition<OrderEvent> BacklogDef = StateDefinition.MessageDeque<OrderEvent>(
        "backlog"
    );

    // The §3.2 burst-batching collections: a per-user "is a batch open" flag and a bounded buffer of
    // the pending messages.
    private static readonly ValueStateDefinition<bool> WindowDef = StateDefinition.Value<bool>("window");
    private static readonly MessageDequeDefinition<Activity> PendingDef = StateDefinition.MessageDeque<Activity>(
        "pending",
        capacity: 100
    );

    [Fact]
    public void Definitions_Construct_AndMapToNative()
    {
        var cart = CartDef.ToNative();
        var totals = TotalsDef.ToNative();
        var backlog = BacklogDef.ToNative();

        Assert.Multiple(
            () => Assert.Equal("cart", cart.Name),
            () => Assert.Equal(Native.StateKind.Value, cart.Kind),
            () => Assert.Equal(Native.StatePayload.Json, cart.Payload),
            () => Assert.Equal(Native.StateKind.Map, totals.Kind),
            () => Assert.Equal(Native.StateKind.Deque, backlog.Kind),
            () => Assert.Equal(Native.StatePayload.Message, backlog.Payload)
        );
    }

    [Fact]
    public void Builder_WithStateCollections_IsChainable()
    {
        var builder = ProsodyClientBuilder.Create().WithStateCollections(CartDef, TotalsDef, BacklogDef);

        Assert.NotNull(builder);
    }

    [Fact]
    public void Handler_Constructs()
    {
        var payload = new OrderEvent("id", 1m);

        Assert.Multiple(
            () => Assert.IsAssignableFrom<IProsodyHandler<OrderEvent>>(new Handler()),
            () =>
                Assert.IsAssignableFrom<IProsodyHandler<Activity>>(
                    new BurstBatchingHandler((_, _) => Task.CompletedTask)
                ),
            () => Assert.Equal("id", payload.OrderId)
        );
    }

    [Fact]
    public void BurstBatching_Builder_IsChainable()
    {
        var activity = new Activity("actor", "liked");

        Assert.Multiple(
            () => Assert.NotNull(ProsodyClientBuilder.Create().WithStateCollections(WindowDef, PendingDef)),
            () => Assert.Equal("actor", activity.Actor),
            () => Assert.Equal(100u, PendingDef.ToNative().Capacity)
        );
    }

    private sealed record Cart(List<string> Items);

    private sealed record OrderEvent(string OrderId, decimal Total);

    private sealed record Activity(string Actor, string Action);

    private sealed class Handler : IProsodyHandler<OrderEvent>
    {
        public async Task OnMessageAsync(
            ProsodyContext prosodyContext,
            Message<OrderEvent> message,
            CancellationToken cancellationToken
        )
        {
            IValueState<Cart> c = prosodyContext.State(CartDef);
            var current = (await c.GetAsync(cancellationToken)).GetValueOrDefault(new Cart([]));
            await c.SetAsync(current with { Items = [.. current.Items, message.Payload!.OrderId] }, cancellationToken);

            IMapState<decimal> t = prosodyContext.State(TotalsDef);
            await t.SetAsync(message.Key, message.Payload.Total, cancellationToken);
            _ = await t.ContainsKeyAsync(message.Key, cancellationToken);
            await foreach (var key in t.EnumerateKeysAsync(ScanDirection.Forward, cancellationToken))
            {
                _ = key;
            }

            await foreach (var (key, total) in t.WithCancellation(cancellationToken))
            {
                _ = key;
                _ = total;
            }

            IDequeState<Message<OrderEvent>> b = prosodyContext.State(BacklogDef);
            await b.PushBackAsync(message, cancellationToken);
            _ = await b.PeekFrontAsync(cancellationToken);
            _ = await b.PeekBackAsync(cancellationToken);
            if ((await b.GetAsync(0, cancellationToken)).TryGetValue(out var oldest))
            {
                _ = oldest.Payload!.OrderId;
            }
        }

        public Task OnExciseAsync(
            ProsodyContext prosodyContext,
            Message<OrderEvent> message,
            CancellationToken cancellationToken
        ) => Task.CompletedTask;

        public Task OnTimerAsync(
            ProsodyContext prosodyContext,
            ProsodyTimer timer,
            CancellationToken cancellationToken
        ) => Task.CompletedTask;
    }

    /// <summary>
    /// The §3.2 burst-batching example: notify on the first event of a batch and set a 5-minute timer,
    /// buffer the rest in a bounded <c>messageDeque</c>, then send one summary when the timer fires.
    /// Compile-pins the fuller README example against the post-Phase-1 surface (the capacity deque and
    /// the concurrent-resolving scan drain).
    /// </summary>
    private sealed class BurstBatchingHandler(Func<string, IReadOnlyList<Message<Activity>>, Task> notify)
        : IProsodyHandler<Activity>
    {
        public async Task OnMessageAsync(
            ProsodyContext prosodyContext,
            Message<Activity> message,
            CancellationToken cancellationToken
        )
        {
            IValueState<bool> window = prosodyContext.State(WindowDef);
            IDequeState<Message<Activity>> pending = prosodyContext.State(PendingDef);
            if (!(await window.GetAsync(cancellationToken)).GetValueOrDefault(false))
            {
                await notify(message.Key, [message]);
                await window.SetAsync(true, cancellationToken);
                // clearAndSchedule (not schedule): timers are not rolled back with state, so a retried
                // event must not stack a second timer — this keeps exactly one.
                await prosodyContext.ClearAndScheduleAsync(DateTimeOffset.UtcNow + TimeSpan.FromMinutes(5));
            }
            else
            {
                await pending.PushBackAsync(message, cancellationToken); // capacity bounds the buffer
            }
        }

        public Task OnExciseAsync(
            ProsodyContext prosodyContext,
            Message<Activity> message,
            CancellationToken cancellationToken
        ) => Task.CompletedTask;

        public async Task OnTimerAsync(
            ProsodyContext prosodyContext,
            ProsodyTimer timer,
            CancellationToken cancellationToken
        )
        {
            IValueState<bool> window = prosodyContext.State(WindowDef);
            IDequeState<Message<Activity>> pending = prosodyContext.State(PendingDef);
            var batch = new List<Message<Activity>>();
            await foreach (var msg in pending.EnumerateAsync(ScanDirection.Forward, cancellationToken))
            {
                batch.Add(msg); // the scan resolves the saved messages concurrently
            }

            if (batch.Count > 0)
            {
                await notify(timer.Key, batch);
            }

            await pending.ClearAsync(cancellationToken);
            await window.ClearAsync(cancellationToken); // close the batch; the next event opens a fresh one
        }
    }
}
