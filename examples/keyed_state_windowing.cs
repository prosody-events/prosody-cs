using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Prosody;
using Prosody.Messaging;
using Prosody.State;

namespace ProsodyExamples;

internal static class KeyedStateWindowing
{
    private readonly record struct Activity(string Actor, string Action);

    private static readonly ValueStateDefinition<bool> Window = StateDefinition.Value<bool>(
        "window",
        ttl: TimeSpan.FromDays(1)
    );
    private static readonly MessageDequeDefinition<Activity> Pending = StateDefinition.MessageDeque<Activity>(
        "pending",
        ttl: TimeSpan.FromDays(1),
        capacity: 100
    );

    public static async Task RunAsync()
    {
        await using var client = ProsodyClientBuilder
            .Create()
            .WithBootstrapServers("localhost:9092")
            .WithGroupId("activity-notifications")
            .WithSubscribedTopics("activity")
            .WithStateCollections(Window, Pending)
            .Build();

        await client.SubscribeAsync(new ActivityHandler());
    }

    private sealed class ActivityHandler : IProsodyHandler<Activity>
    {
        public async Task OnMessageAsync(
            ProsodyContext prosodyContext,
            Message<Activity> message,
            CancellationToken cancellationToken
        )
        {
            var window = prosodyContext.State(Window);
            var pending = prosodyContext.State(Pending);

            if ((await window.GetAsync(cancellationToken)).GetValueOrDefault(false))
            {
                await pending.PushBackAsync(message, cancellationToken);
                return;
            }

            await NotifyAsync(message.Key, [message]);
            await window.SetAsync(true, cancellationToken);
            await prosodyContext.ClearAndScheduleAsync(DateTimeOffset.UtcNow + TimeSpan.FromMinutes(5));
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
            var pending = prosodyContext.State(Pending);
            var batch = new List<Message<Activity>>();
            await foreach (var message in pending.EnumerateAsync(ScanDirection.Forward, cancellationToken))
            {
                batch.Add(message);
            }

            if (batch.Count > 0)
            {
                await NotifyAsync(timer.Key, batch);
            }

            await pending.ClearAsync(cancellationToken);
            await prosodyContext.State(Window).ClearAsync(cancellationToken);
        }
    }

    private static Task NotifyAsync(string userId, IReadOnlyList<Message<Activity>> activities)
    {
        Console.WriteLine($"Notify {userId} about {activities.Count} activities");
        return Task.CompletedTask;
    }
}
