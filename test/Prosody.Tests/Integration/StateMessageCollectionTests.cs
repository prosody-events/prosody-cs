using Prosody.Messaging;
using Prosody.State;
using Prosody.Tests.TestHelpers;

namespace Prosody.Tests.Integration;

/// <summary>
/// Integration tests for message-flavoured keyed-state collections against real Kafka and Cassandra
/// (appendix-1 item 4): the handled message is recorded in event 1 and read/scanned back in event 2
/// with topic, partition, offset, key, and payload equal to the original. <see cref="Message{T}"/>
/// has no record equality, so fields are compared individually.
/// </summary>
/// <remarks>
/// The recorded message is deliberately pushed past offset 3 (the highest partition index on the
/// 4-partition test topic) by preceding warm-up sends on the same key, so the recorded message's
/// partition and offset differ. A message at offset 0 on partition 0 would let a partition/offset
/// transposition in the read-back reconstruction pass unnoticed.
/// </remarks>
public sealed class StateMessageCollectionTests(IntegrationTestFixture fixture) : IntegrationTestBase(fixture)
{
    // Sends past offset 3 so the recorded message's offset cannot equal any partition index.
    private const int _warmupCount = 5;

    private sealed record MsgFields
    {
        public string Topic { get; init; } = "";
        public string Key { get; init; } = "";
        public int Partition { get; init; }
        public long Offset { get; init; }
        public string Content { get; init; } = "";

        public static MsgFields From(Message<StateMessagePayload> message) =>
            new()
            {
                Topic = message.Topic,
                Key = message.Key,
                Partition = message.Partition,
                Offset = message.Offset,
                Content = message.Payload?.Content ?? "",
            };
    }

    /// <summary>
    /// Sends <see cref="_warmupCount"/> ignored warm-up messages, then the message to record
    /// (sequence 1), then the read-back trigger (sequence 2), all to a single key so they land on one
    /// partition in offset order. The recorded message therefore sits at offset <see cref="_warmupCount"/>.
    /// </summary>
    private static async Task SendWarmupRecordReadbackAsync(IntegrationTestContext ctx, string recordContent)
    {
        var key = TopicGenerator.GenerateKey();
        for (var i = 0; i < _warmupCount; i++)
        {
            await ctx.Client.SendAsync(
                ctx.Topic,
                key,
                new StateMessagePayload { Content = "warmup", Sequence = 0 },
                TestContext.Current.CancellationToken
            );
        }

        await ctx.Client.SendAsync(
            ctx.Topic,
            key,
            new StateMessagePayload { Content = recordContent, Sequence = 1 },
            TestContext.Current.CancellationToken
        );
        await ctx.Client.SendAsync(
            ctx.Topic,
            key,
            new StateMessagePayload { Content = "later", Sequence = 2 },
            TestContext.Current.CancellationToken
        );
    }

    // Guards that the recorded coordinates are distinguishable, so AssertSameMessage's separate
    // partition and offset checks can actually observe a transposition of the two.
    private static void AssertDistinguishableCoordinates(MsgFields original) =>
        Assert.Multiple(
            () =>
                Assert.True(
                    original.Offset > 3,
                    $"fixture offset {original.Offset} must exceed the max partition index (3) so a partition/offset swap is observable"
                ),
            () => Assert.NotEqual(original.Partition, (int)original.Offset)
        );

    private static void AssertSameMessage(MsgFields original, MsgFields readBack) =>
        Assert.Multiple(
            () => Assert.Equal(original.Topic, readBack.Topic),
            () => Assert.Equal(original.Key, readBack.Key),
            () => Assert.Equal(original.Partition, readBack.Partition),
            () => Assert.Equal(original.Offset, readBack.Offset),
            () => Assert.Equal(original.Content, readBack.Content)
        );

    [Fact(Timeout = 60_000)]
    public async Task MessageValue_StoresAndReadsBackIntact()
    {
        await using var ctx = await CreateTestContextAsync(StateTestSupport.WithAllCollections());
        var originals = new MessageChannel<MsgFields>();
        var readBacks = new MessageChannel<MsgFields>();

        var handler = new TestProsodyHandler<StateMessagePayload>(
            onMessage: async (context, msg, ct) =>
            {
                var last = context.State(StateTestSupport.LastMsg);
                if (msg.Payload?.Sequence == 1)
                {
                    await last.SetAsync(msg, ct);
                    originals.Send(MsgFields.From(msg));
                    return;
                }

                if (msg.Payload?.Sequence != 2)
                {
                    return;
                }

                var got = await last.GetAsync(ct);
                readBacks.Send(MsgFields.From(got.Value));
            }
        );

        await ctx.Client.SubscribeAsync(handler);
        await SendWarmupRecordReadbackAsync(ctx, "one");

        var original = await originals.ReceiveAsync(
            IntegrationTestFixture.DefaultTimeout,
            TestContext.Current.CancellationToken
        );
        var readBack = await readBacks.ReceiveAsync(
            IntegrationTestFixture.DefaultTimeout,
            TestContext.Current.CancellationToken
        );

        AssertDistinguishableCoordinates(original);
        AssertSameMessage(original, readBack);
        Assert.Equal("one", readBack.Content);
    }

    [Fact(Timeout = 60_000)]
    public async Task MessageMap_RoundTripsUnderStringKeys()
    {
        await using var ctx = await CreateTestContextAsync(StateTestSupport.WithAllCollections());
        var originals = new MessageChannel<MsgFields>();
        var readBacks = new MessageChannel<MsgFields>();

        var handler = new TestProsodyHandler<StateMessagePayload>(
            onMessage: async (context, msg, ct) =>
            {
                var index = context.State(StateTestSupport.MsgIndex);
                if (msg.Payload?.Sequence == 1)
                {
                    await index.SetAsync("m1", msg, ct);
                    originals.Send(MsgFields.From(msg));
                    return;
                }

                if (msg.Payload?.Sequence != 2)
                {
                    return;
                }

                var got = await index.GetAsync("m1", ct);
                readBacks.Send(MsgFields.From(got.Value));
            }
        );

        await ctx.Client.SubscribeAsync(handler);
        await SendWarmupRecordReadbackAsync(ctx, "indexed");

        var original = await originals.ReceiveAsync(
            IntegrationTestFixture.DefaultTimeout,
            TestContext.Current.CancellationToken
        );
        var readBack = await readBacks.ReceiveAsync(
            IntegrationTestFixture.DefaultTimeout,
            TestContext.Current.CancellationToken
        );

        AssertDistinguishableCoordinates(original);
        AssertSameMessage(original, readBack);
    }

    [Fact(Timeout = 60_000)]
    public async Task MessageDeque_RoundTripsThroughPushGetScan()
    {
        await using var ctx = await CreateTestContextAsync(StateTestSupport.WithAllCollections());
        var originals = new MessageChannel<MsgFields>();
        var getBacks = new MessageChannel<MsgFields>();
        var scanBacks = new MessageChannel<MsgFields>();

        var handler = new TestProsodyHandler<StateMessagePayload>(
            onMessage: async (context, msg, ct) =>
            {
                var log = context.State(StateTestSupport.MsgLog);
                if (msg.Payload?.Sequence == 1)
                {
                    await log.PushBackAsync(msg, ct);
                    originals.Send(MsgFields.From(msg));
                    return;
                }

                if (msg.Payload?.Sequence != 2)
                {
                    return;
                }

                var got = await log.GetAsync(0, ct);
                getBacks.Send(MsgFields.From(got.Value));
                await foreach (var element in log.EnumerateAsync(ScanDirection.Forward, ct))
                {
                    scanBacks.Send(MsgFields.From(element));
                }
            }
        );

        await ctx.Client.SubscribeAsync(handler);
        await SendWarmupRecordReadbackAsync(ctx, "logged");

        var original = await originals.ReceiveAsync(
            IntegrationTestFixture.DefaultTimeout,
            TestContext.Current.CancellationToken
        );
        var getBack = await getBacks.ReceiveAsync(
            IntegrationTestFixture.DefaultTimeout,
            TestContext.Current.CancellationToken
        );
        var scanBack = await scanBacks.ReceiveAsync(
            IntegrationTestFixture.DefaultTimeout,
            TestContext.Current.CancellationToken
        );

        AssertDistinguishableCoordinates(original);
        Assert.Multiple(() => AssertSameMessage(original, getBack), () => AssertSameMessage(original, scanBack));
    }
}
