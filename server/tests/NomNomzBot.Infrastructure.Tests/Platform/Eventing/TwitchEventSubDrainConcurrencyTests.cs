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
        drain.IsCompleted.Should().BeFalse("the slow notification is still in flight");

        slowGate.SetResult();
        await drain;

        dispatcher.DispatchedTwitchIds.Should().Contain(["twitch-slow", "twitch-fast"]);
        inbox.RemovedIds.Should().HaveCount(2);
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
        FakeEventSubInbox inbox,
        ControllableDispatcher dispatcher
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

    private sealed class FakeEventSubInbox(IReadOnlyList<EventSubInboxMessage> messages)
        : IEventSubInbox
    {
        private readonly Queue<IReadOnlyList<EventSubInboxMessage>> _batches = new([messages, []]);
        private readonly List<Guid> _removed = [];

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
