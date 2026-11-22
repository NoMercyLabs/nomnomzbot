// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot. You may use, change and share it for free,
//  for any noncommercial purpose, under the PolyForm Noncommercial License
//  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
//  See LICENSE for details, and TRADEMARKS.md for the name and logo.
//
//  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// -----------------------------------------------------------------------------

using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Abstractions.Transport;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.DTOs.Twitch.EventSub;
using NomNomzBot.Domain.Platform.Entities;
using NomNomzBot.Infrastructure.Platform.Eventing;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Platform.Eventing;

/// <summary>
/// S-CONCURRENCY-DRAIN: the owner's live report — a long TTS line generating for one chat message held up an
/// unrelated Spotify command (<c>!banger</c>) that arrived right after it, "they should be working at the
/// same time and not stack up". Root cause: <see cref="TwitchEventSubHostedService.DrainBatchAsync"/> used to
/// await each inbox notification fully, one at a time, before even looking at the next. This proves the fix:
/// a fast notification queued behind a slow one completes WHILE the slow one is still in flight.
/// </summary>
public sealed class TwitchEventSubDrainConcurrencyTests
{
    [Fact]
    public async Task FastNotification_QueuedBehindASlowOne_CompletesWhileTheSlowOneIsStillRunning()
    {
        TaskCompletionSource slowGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource fastCompleted = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        TaskCompletionSource slowStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);

        ControllableDispatcher dispatcher = new(slowGate.Task, slowStarted, fastCompleted);
        FakeEventSubInbox inbox = new([
            InboxMessageFor("msg-slow", "twitch-slow"),
            InboxMessageFor("msg-fast", "twitch-fast"),
        ]);

        TwitchEventSubHostedService service = BuildService(inbox, dispatcher);

        Task drain = service.DrainBatchAsync(CancellationToken.None);

        // The fast message must finish BEFORE the slow one is released — if the drain loop still awaited
        // each message sequentially, this would deadlock here (the slow one never releases on its own).
        Task won = await Task.WhenAny(fastCompleted.Task, Task.Delay(TimeSpan.FromSeconds(5)));
        won.Should()
            .Be(
                fastCompleted.Task,
                "the fast notification must not be stuck in line behind the still-running slow one"
            );
        slowGate.SetResult();
        await drain;
        await inbox.BothRemoved;

        dispatcher.DispatchedTwitchIds.Should().Contain(["twitch-slow", "twitch-fast"]);
        inbox.RemovedIds.Should().HaveCount(2);
    }

    /// <summary>
    /// The owner's live report of 2026-11-22: <c>!raid</c> ran a pipeline with an 88-second wait, and Twitch's
    /// raid start notification, which arrived one second later, was handled only after that wait. The drain
    /// kept the poll loop until every notification of its batch had finished, so nothing that arrived later
    /// was looked at.
    /// </summary>
    [Fact]
    public async Task ANotificationThatArrivesWhileASlowOneRuns_IsDispatchedBeforeTheSlowOneEnds()
    {
        TaskCompletionSource slowGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource lateCompleted = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        TaskCompletionSource slowStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);

        ControllableDispatcher dispatcher = new(slowGate.Task, slowStarted, lateCompleted);
        EventSubInboxMessage slow = InboxMessageFor("msg-slow", "twitch-slow");
        EventSubInboxMessage late = InboxMessageFor("msg-late", "twitch-fast");
        // The slow one stays a row until it ends, so the second poll sees it again next to the late one.
        FakeEventSubInbox inbox = new([slow], [], [slow, late]);

        TwitchEventSubHostedService service = BuildService(inbox, dispatcher);

        Task firstPoll = service.DrainBatchAsync(CancellationToken.None);
        await slowStarted.Task;
        Task pollReturned = await Task.WhenAny(firstPoll, Task.Delay(TimeSpan.FromSeconds(5)));
        pollReturned
            .Should()
            .Be(firstPoll, "the poll loop must be free again while the slow notification runs");

        await service.DrainBatchAsync(CancellationToken.None);
        Task won = await Task.WhenAny(lateCompleted.Task, Task.Delay(TimeSpan.FromSeconds(5)));
        won.Should().Be(lateCompleted.Task, "the late notification must not wait for the slow one");
        dispatcher.DispatchedTwitchIds.Should().Equal("twitch-fast");

        slowGate.SetResult();
        await inbox.BothRemoved;

        dispatcher
            .DispatchedTwitchIds.Should()
            .Equal(
                ["twitch-fast", "twitch-slow"],
                "the slow notification is started one time only"
            );
        inbox.RemovedIds.Should().BeEquivalentTo([slow.Id, late.Id]);
    }

    /// <summary>
    /// A notification whose dispatch fails keeps its inbox row for a retry. That retry belongs to the next
    /// poll: one poll that restarts it at once would spin on a failure that lasts (a database outage).
    /// </summary>
    [Fact]
    public async Task ANotificationThatFails_IsStartedOneTimePerPoll()
    {
        FailingDispatcher dispatcher = new();
        EventSubInboxMessage broken = InboxMessageFor("msg-broken", "twitch-broken");
        RepeatingInbox inbox = new(broken, peeks: 5);

        TwitchEventSubHostedService service = BuildService(inbox, dispatcher);

        await service.DrainBatchAsync(CancellationToken.None);

        dispatcher
            .Attempts.Should()
            .Be(1, "the retry of a failed notification waits for the next poll");
        inbox.RemovedIds.Should().BeEmpty("a failed notification keeps its row");
    }

    private static EventSubInboxMessage InboxMessageFor(
        string messageId,
        string twitchBroadcasterUserId
    ) =>
        new()
        {
            MessageId = messageId,
            MessageTimestamp = new DateTime(2026, 11, 5, 12, 0, 0, DateTimeKind.Utc),
            SubscriptionType = "channel.chat.message",
            SubscriptionVersion = "1",
            TwitchBroadcasterUserId = twitchBroadcasterUserId,
            EventJson = "{}",
        };

    private static TwitchEventSubHostedService BuildService(
        IEventSubInbox inbox,
        INotificationDispatcher dispatcher
    )
    {
        ITwitchIdentityResolver resolver = Substitute.For<ITwitchIdentityResolver>();
        resolver
            .GetBroadcasterIdAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Guid?>(Guid.NewGuid()));

        IServiceProvider provider = new ServiceCollection()
            .AddScoped(_ => resolver)
            .AddScoped<INotificationDispatcher>(_ => dispatcher)
            .BuildServiceProvider();

        return new TwitchEventSubHostedService(
            new RootServiceScopeFactory(provider),
            Substitute.For<IEventSubTransport>(),
            new EventSubConditionBuilder(),
            Substitute.For<NomNomzBot.Domain.Platform.Interfaces.IEventBus>(),
            new FakeTimeProvider(new DateTimeOffset(2026, 11, 5, 12, 0, 0, TimeSpan.Zero)),
            NullLogger<TwitchEventSubHostedService>.Instance,
            conduits: null,
            inbox: inbox
        );
    }

    /// <summary>Dispatches "twitch-slow" only once <paramref name="release"/> completes; everything else
    /// (including "twitch-fast") completes immediately — mirrors a long TTS synthesis blocking one command
    /// while an unrelated Spotify command should run straight through.</summary>
    private sealed class ControllableDispatcher(
        Task release,
        TaskCompletionSource slowStarted,
        TaskCompletionSource fastCompleted
    ) : INotificationDispatcher
    {
        private readonly List<string> _dispatched = [];

        public IReadOnlyList<string> DispatchedTwitchIds
        {
            get
            {
                lock (_dispatched)
                    return [.. _dispatched];
            }
        }

        public async Task<Result<NotificationDispatchResult>> DispatchAsync(
            EventSubNotification notification,
            CancellationToken ct = default
        )
        {
            if (notification.TwitchBroadcasterUserId == "twitch-slow")
            {
                slowStarted.TrySetResult();
                await release;
            }

            lock (_dispatched)
                _dispatched.Add(notification.TwitchBroadcasterUserId);

            if (notification.TwitchBroadcasterUserId == "twitch-fast")
                fastCompleted.TrySetResult();

            return Result.Success(new NotificationDispatchResult(Guid.NewGuid(), 1, false));
        }
    }

    /// <summary>Answers each peek with the next batch, then with an empty inbox.</summary>
    private sealed class FakeEventSubInbox(params IReadOnlyList<EventSubInboxMessage>[] batches)
        : IEventSubInbox
    {
        private readonly Queue<IReadOnlyList<EventSubInboxMessage>> _batches = new(batches);
        private readonly List<Guid> _removed = [];
        private readonly TaskCompletionSource _bothRemoved = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );

        public Task BothRemoved => _bothRemoved.Task;

        public IReadOnlyList<Guid> RemovedIds
        {
            get
            {
                lock (_removed)
                    return [.. _removed];
            }
        }

        public Task<bool> EnqueueAsync(
            EventSubInboxMessage message,
            CancellationToken ct = default
        ) => Task.FromResult(true);

        public Task<IReadOnlyList<EventSubInboxMessage>> PeekAsync(
            int max,
            CancellationToken ct = default
        ) =>
            Task.FromResult(
                _batches.Count > 0 ? _batches.Dequeue() : (IReadOnlyList<EventSubInboxMessage>)[]
            );

        public Task RemoveAsync(Guid id, CancellationToken ct = default)
        {
            lock (_removed)
            {
                _removed.Add(id);
                if (_removed.Count == 2)
                    _bothRemoved.TrySetResult();
            }
            return Task.CompletedTask;
        }
    }

    /// <summary>Fails every dispatch and counts the attempts.</summary>
    private sealed class FailingDispatcher : INotificationDispatcher
    {
        private int _attempts;

        public int Attempts => Volatile.Read(ref _attempts);

        public Task<Result<NotificationDispatchResult>> DispatchAsync(
            EventSubNotification notification,
            CancellationToken ct = default
        )
        {
            Interlocked.Increment(ref _attempts);
            throw new InvalidOperationException("the dispatch failed");
        }
    }

    /// <summary>Shows the same row on a fixed number of peeks, each after a short real wait, so a failed
    /// dispatch has ended before the next peek.</summary>
    private sealed class RepeatingInbox(EventSubInboxMessage message, int peeks) : IEventSubInbox
    {
        private readonly List<Guid> _removed = [];
        private int _peeksLeft = peeks;

        public IReadOnlyList<Guid> RemovedIds
        {
            get
            {
                lock (_removed)
                    return [.. _removed];
            }
        }

        public Task<bool> EnqueueAsync(
            EventSubInboxMessage message,
            CancellationToken ct = default
        ) => Task.FromResult(true);

        public async Task<IReadOnlyList<EventSubInboxMessage>> PeekAsync(
            int max,
            CancellationToken ct = default
        )
        {
            await Task.Delay(TimeSpan.FromMilliseconds(50), ct);
            return Interlocked.Decrement(ref _peeksLeft) >= 0 ? [message] : [];
        }

        public Task RemoveAsync(Guid id, CancellationToken ct = default)
        {
            lock (_removed)
                _removed.Add(id);
            return Task.CompletedTask;
        }
    }

    /// <summary>A real <see cref="IServiceScopeFactory"/> whose scopes all share one root provider — enough
    /// for this test, which only resolves stateless fakes per scope.</summary>
    private sealed class RootServiceScopeFactory(IServiceProvider provider) : IServiceScopeFactory
    {
        public IServiceScope CreateScope() => provider.CreateScope();
    }
}
