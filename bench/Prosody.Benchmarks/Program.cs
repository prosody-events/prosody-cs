using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Prosody;
using Prosody.Messaging;

const int MessageCount = 20_000;
const uint ConcurrentKeyCount = 1_000;
const uint MaxUncommitted = 2_000;

var bootstrap = Environment.GetEnvironmentVariable("PROSODY_BOOTSTRAP_SERVERS") ?? "localhost:9094";
var cassandra = Environment.GetEnvironmentVariable("PROSODY_CASSANDRA_NODES") ?? "localhost:9042";
var keyspace = Environment.GetEnvironmentVariable("PROSODY_CASSANDRA_KEYSPACE") ?? "prosody_test";
var runId = Guid.NewGuid().ToString("N");
var topic = $"ffi-comparison-{runId}";
var groupId = $"ffi-comparison-{runId}";

using var admin = new AdminClient(bootstrap);
await admin.CreateTopicAsync(topic, 32, 1);
await using var client = await ProsodyClientBuilder
    .Create()
    .WithBootstrapServers(bootstrap)
    .WithGroupId(groupId)
    .WithSubscribedTopics(topic)
    .WithSourceSystem("ffi-comparison")
    .WithMaxConcurrency(ConcurrentKeyCount)
    .Configure(options =>
    {
        options.MaxUncommitted = MaxUncommitted;
        options.CommitInterval = TimeSpan.FromMilliseconds(100);
        options.CassandraNodes = [cassandra];
        options.CassandraKeyspace = keyspace;
        options.PeerBindAddress = new IPEndPoint(IPAddress.Loopback, 0);
        options.ProbePort = 0;
    })
    .BuildAsync();
var handler = new CountingHandler(MessageCount);
await client.SubscribeAsync(handler);
GC.Collect();
GC.WaitForPendingFinalizers();
GC.Collect();
var allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
var stopwatch = Stopwatch.StartNew();
for (var start = 0; start < MessageCount; start += (int)ConcurrentKeyCount)
{
    var count = Math.Min((int)ConcurrentKeyCount, MessageCount - start);
    var sends = new Task[count];
    for (var offset = 0; offset < count; offset++)
    {
        var index = start + offset;
        sends[offset] = client.SendAsync(topic, $"key-{index % ConcurrentKeyCount}", CreatePayload(index));
    }
    await Task.WhenAll(sends);
}
await handler.Completion.WaitAsync(TimeSpan.FromMinutes(5));
await WaitForZeroLagAsync(groupId, MessageCount, TimeSpan.FromMinutes(1));
stopwatch.Stop();

if (handler.InvalidPayloads != 0)
{
    throw new InvalidOperationException($"Received {handler.InvalidPayloads} invalid payloads.");
}

var report = new Report(
    MessageCount,
    ConcurrentKeyCount,
    MaxUncommitted,
    PayloadSizes.Minimum,
    PayloadSizes.Maximum,
    MessageCount / stopwatch.Elapsed.TotalSeconds,
    GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore,
    Process.GetCurrentProcess().WorkingSet64
);
Console.WriteLine(JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));

static async Task WaitForZeroLagAsync(string groupId, int expectedOffsets, TimeSpan timeout)
{
    var deadline = Stopwatch.StartNew();
    while (deadline.Elapsed < timeout)
    {
        using var process =
            Process.Start(
                new ProcessStartInfo(
                    "docker",
                    $"exec prosody-kafka-1 /opt/kafka/bin/kafka-consumer-groups.sh --bootstrap-server localhost:9092 --group {groupId} --describe"
                )
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                }
            ) ?? throw new InvalidOperationException("Failed to start the Kafka lag check.");
        var output = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();
        var offsets = 0L;
        var lag = 0L;
        foreach (var line in output.Split('\n'))
        {
            var columns = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (
                columns.Length >= 6
                && columns[0] == groupId
                && long.TryParse(columns[3], out var currentOffset)
                && long.TryParse(columns[5], out var partitionLag)
            )
            {
                offsets += currentOffset;
                lag += partitionLag;
            }
        }
        if (offsets == expectedOffsets && lag == 0)
        {
            return;
        }
        await Task.Delay(TimeSpan.FromMilliseconds(100));
    }
    throw new TimeoutException($"Kafka group {groupId} did not reach zero lag.");
}

static Payload CreatePayload(int sequence)
{
    var targetLength = PayloadSizes.Target(sequence, MessageCount);
    var emptyLength = JsonSerializer.SerializeToUtf8Bytes(new Payload(sequence, string.Empty)).Length;
    var dataLength = targetLength - emptyLength;
    var randomBytes = RandomNumberGenerator.GetBytes((dataLength + 1) / 2);
    return new Payload(sequence, Convert.ToHexString(randomBytes)[..dataLength]);
}

internal sealed record Payload(int Sequence, string Data);

internal static class PayloadSizes
{
    internal const int Minimum = 1 * 1024;
    internal const int Maximum = 200 * 1024;

    internal static int Target(int sequence, int messageCount) =>
        Minimum + (int)((long)sequence * (Maximum - Minimum) / (messageCount - 1));
}

internal sealed record Report(
    int MessageCount,
    uint ConcurrentKeyCount,
    uint MaxUncommitted,
    int MinimumPayloadBytes,
    int MaximumPayloadBytes,
    double OperationsPerSecond,
    long AllocatedBytes,
    long WorkingSetBytes
);

internal sealed class CountingHandler(int expectedMessages) : IProsodyHandler<Payload>
{
    private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _messages;
    private int _invalidPayloads;

    internal Task Completion => _completion.Task;
    internal int InvalidPayloads => Volatile.Read(ref _invalidPayloads);

    public Task OnMessageAsync(ProsodyContext context, Message<Payload> message, CancellationToken cancellationToken)
    {
        var payload = message.Payload;
        if (
            payload is null
            || payload.Sequence < 0
            || payload.Sequence >= expectedMessages
            || JsonSerializer.SerializeToUtf8Bytes(payload).Length
                != PayloadSizes.Target(payload.Sequence, expectedMessages)
        )
        {
            Interlocked.Increment(ref _invalidPayloads);
        }
        if (Interlocked.Increment(ref _messages) == expectedMessages)
        {
            _completion.TrySetResult();
        }
        return Task.CompletedTask;
    }

    public Task OnExciseAsync(ProsodyContext context, ExciseMessage message, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public Task OnTimerAsync(ProsodyContext context, ProsodyTimer timer, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}
