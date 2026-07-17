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
            () => Assert.Equal("id", payload.OrderId)
        );
    }

    private sealed record Cart(List<string> Items);

    private sealed record OrderEvent(string OrderId, decimal Total);

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
            await foreach (var (key, total) in t.WithCancellation(cancellationToken))
            {
                _ = key;
                _ = total;
            }

            IDequeState<Message<OrderEvent>> b = prosodyContext.State(BacklogDef);
            await b.PushBackAsync(message, cancellationToken);
            if ((await b.GetAsync(0, cancellationToken)).TryGetValue(out var oldest))
            {
                _ = oldest.Payload!.OrderId;
            }
        }

        public Task OnTimerAsync(
            ProsodyContext prosodyContext,
            ProsodyTimer timer,
            CancellationToken cancellationToken
        ) => Task.CompletedTask;
    }
}
