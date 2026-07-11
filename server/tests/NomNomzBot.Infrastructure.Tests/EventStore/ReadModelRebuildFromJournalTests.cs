// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Abstractions.Auth;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Analytics;
using NomNomzBot.Application.Contracts.EventStore;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Domain.Community.Events;
using NomNomzBot.Domain.Platform;
using NomNomzBot.Domain.Rewards.Events;
using NomNomzBot.Infrastructure.Analytics;
using NomNomzBot.Infrastructure.EventStore;
using NomNomzBot.Infrastructure.Identity;
using NomNomzBot.Infrastructure.Tests.Identity;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.EventStore;

/// <summary>
/// The whole-read-model replay proof for the owner's question — "does a replay rebuild the WHOLE read model, not
/// just one projection?". A representative spread of real domain events is appended to the journal, then EVERY
/// registered analytics projection (channel-daily, message-activity-daily, viewer-engagement-daily, viewer-profile,
/// watch-session) is folded once. The entire read model is snapshotted, the tables are corrupted, and each
/// projection is rebuilt (<c>ResetAsync</c> → replay from position 0). The rebuilt read model must equal the
/// original snapshot byte-for-byte — proving every projection reconstructs purely from <c>EventJournal</c>, with no
/// live Twitch call. This complements <see cref="ProjectionRunnerTests"/> (which proves the mechanism on one
/// synthetic projection) by spanning the real projection set against a real relational journal.
/// </summary>
public sealed class ReadModelRebuildFromJournalTests
{
    private static readonly FakeTimeProvider Clock = new(
        new DateTimeOffset(2026, 6, 22, 20, 0, 0, TimeSpan.Zero)
    );
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-0000000abc01");
    private static readonly DateTime Live = new(2026, 6, 22, 20, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Reset_then_replay_reconstructs_the_entire_read_model_from_the_journal_alone()
    {
        using ReadModelRebuildDatabase database = ReadModelRebuildDatabase.Open();
        await using ReadModelRebuildDbContext db = database.NewContext();

        EventJournalService journal = new(
            db,
            new TenantSequenceAllocator(db),
            new RebuildTestUnitOfWork(db),
            Clock
        );

        await AppendRepresentativeSpreadAsync(journal);

        List<IProjection> projections = BuildProjections(db);
        ProjectionRunner runner = new(
            projections,
            journal,
            new EventUpcasterRegistry([]),
            db,
            Clock
        );

        // Incremental fold of every projection to the head, then snapshot the whole read model.
        foreach (IProjection projection in projections)
        {
            Result<long> applied = await runner.RunOnceAsync(projection.Name, Channel);
            applied.IsSuccess.Should().BeTrue(applied.ErrorMessage);
        }

        ReadModelSnapshot original = await SnapshotAsync(db);
        original.AssertNonTrivial();

        // Corrupt every read-model table, then rebuild each projection from zero. Reset wipes the table, replay
        // re-derives it purely from the journal.
        await CorruptEveryReadModelAsync(db);

        foreach (IProjection projection in projections)
        {
            Result<long> rebuilt = await runner.RebuildAsync(projection.Name, Channel);
            rebuilt.IsSuccess.Should().BeTrue(rebuilt.ErrorMessage);
        }

        ReadModelSnapshot rebuiltModel = await SnapshotAsync(db);
        rebuiltModel
            .Should()
            .BeEquivalentTo(
                original,
                "the entire read model must reconstruct identically from EventJournal alone"
            );
    }

    // A representative spread: every event type the analytics projections subscribe to, for two distinct viewers,
    // inside the live window so watch-sessions open. Appended via the real journal so they get real StreamPositions.
    private static async Task AppendRepresentativeSpreadAsync(EventJournalService journal)
    {
        List<DomainEventBase> events =
        [
            Chat("100", "alice", "Alice", Live),
            Chat("100", "alice", "Alice", Live.AddSeconds(90)),
            Chat("200", "bob", "Bob", Live.AddSeconds(120)),
            new FollowEvent
            {
                BroadcasterId = Channel,
                OccurredAt = new DateTimeOffset(Live, TimeSpan.Zero),
                UserId = "100",
                UserDisplayName = "Alice",
                UserLogin = "alice",
                FollowedAt = new DateTimeOffset(Live, TimeSpan.Zero),
            },
            new NewSubscriptionEvent
            {
                BroadcasterId = Channel,
                OccurredAt = new DateTimeOffset(Live, TimeSpan.Zero),
                UserId = "200",
                UserDisplayName = "Bob",
                Tier = "1000",
            },
            new GiftSubscriptionEvent
            {
                BroadcasterId = Channel,
                OccurredAt = new DateTimeOffset(Live, TimeSpan.Zero),
                GifterUserId = "100",
                GifterDisplayName = "Alice",
                Tier = "1000",
                GiftCount = 2,
                IsAnonymous = false,
                Recipients = [],
            },
            new CheerEvent
            {
                BroadcasterId = Channel,
                OccurredAt = new DateTimeOffset(Live, TimeSpan.Zero),
                UserId = "200",
                UserDisplayName = "Bob",
                Bits = 150,
                Message = "Cheer150",
                IsAnonymous = false,
            },
            Command("100", "alice", Live.AddSeconds(30)),
            Reward("100", "Alice", Live.AddSeconds(60)),
        ];

        foreach (DomainEventBase @event in events)
        {
            AppendEventRequest request = new(
                EventId: @event.EventId,
                BroadcasterId: Channel,
                EventType: @event.GetType().Name,
                EventVersion: 1,
                Source: "domain",
                PayloadJson: Newtonsoft.Json.JsonConvert.SerializeObject(@event),
                MetadataJson: "{}",
                OccurredAt: @event.OccurredAt.UtcDateTime
            );
            Result<EventRecord> appended = await journal.AppendAsync(request);
            appended.IsSuccess.Should().BeTrue(appended.ErrorMessage);
        }
    }

    private static List<IProjection> BuildProjections(ReadModelRebuildDbContext db)
    {
        ICurrentUserService currentUser = Substitute.For<ICurrentUserService>();

        // UserService.GetOrCreateAsync creates its own scope per call (concurrency safety — see commit 0294b46).
        // Provide a real IServiceScopeFactory whose scopes resolve IApplicationDbContext to the test's db so
        // the scoped get-or-create can find Users without hitting a missing-registration exception.
        ServiceCollection services = new();
        services.AddSingleton<IApplicationDbContext>(db);
        ServiceProvider provider = services.BuildServiceProvider();
        IServiceScopeFactory scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();

        IUserService userService = AuthTestBuilder.UserService(db, currentUser, scopeFactory);
        ViewerResolver resolver = new(db, userService);

        // Activity always falls inside one live window so watch-sessions open during replay exactly as on the
        // incremental fold (the resolver is deterministic, so reset→replay is identical).
        ILiveWindowResolver liveWindow = Substitute.For<ILiveWindowResolver>();
        liveWindow
            .GetCoveringStreamIdAsync(
                Arg.Any<Guid>(),
                Arg.Any<DateTime>(),
                Arg.Any<CancellationToken>()
            )
            .Returns("stream-1");

        return
        [
            new ChannelAnalyticsDailyProjection(db, liveWindow),
            new MessageActivityDailyProjection(db, resolver),
            new ViewerEngagementDailyProjection(db, resolver),
            new ViewerProfileProjection(db, resolver),
            new WatchSessionProjection(db, resolver, liveWindow),
        ];
    }

    // ── read-model snapshot (the observable whole-model state) ──
    private static async Task<ReadModelSnapshot> SnapshotAsync(ReadModelRebuildDbContext db)
    {
        List<string> channelDaily = await db
            .ChannelAnalyticsDailies.AsNoTracking()
            .OrderBy(r => r.ActivityDate)
            .Select(r =>
                $"{r.ActivityDate}|msg={r.TotalMessages}|fol={r.NewFollowers}|sub={r.NewSubscribers}|bits={r.BitsCheered}|cmd={r.CommandsRun}|red={r.RedemptionsCount}"
            )
            .ToListAsync();

        List<string> messageDaily = await db
            .MessageActivityDailies.AsNoTracking()
            .OrderBy(r => r.ViewerUserId)
            .Select(r => $"{r.ViewerUserId}|count={r.MessageCount}")
            .ToListAsync();

        List<string> engagement = await db
            .ViewerEngagementDailies.AsNoTracking()
            .OrderBy(r => r.ViewerUserId)
            .Select(r =>
                $"{r.ViewerUserId}|msg={r.MessageCount}|cmd={r.CommandCount}|red={r.RedemptionCount}"
            )
            .ToListAsync();

        List<string> profiles = await db
            .ViewerProfiles.AsNoTracking()
            .OrderBy(r => r.ViewerTwitchUserId)
            .Select(r =>
                $"{r.ViewerTwitchUserId}|name={r.DisplayNameSnapshot}|msg={r.TotalMessages}|sub={r.IsSubscriber}"
            )
            .ToListAsync();

        List<string> sessions = await db
            .WatchSessions.AsNoTracking()
            .OrderBy(r => r.ViewerUserId)
            .ThenBy(r => r.StartedAt)
            .Select(r =>
                $"{r.ViewerUserId}|stream={r.StreamId}|dur={r.DurationSeconds}|msgs={r.MessageCountInSession}|present={r.PresenceConfirmed}"
            )
            .ToListAsync();

        List<string> viewers = await db
            .Users.AsNoTracking()
            .OrderBy(u => u.TwitchUserId)
            .Select(u => $"{u.TwitchUserId}|{u.DisplayName}")
            .ToListAsync();

        return new ReadModelSnapshot(
            channelDaily,
            messageDaily,
            engagement,
            profiles,
            sessions,
            viewers
        );
    }

    // Mutate every table to a wrong value so an empty/unchanged rebuild can never pass by accident.
    private static async Task CorruptEveryReadModelAsync(ReadModelRebuildDbContext db)
    {
        foreach (
            NomNomzBot.Domain.Analytics.Entities.ChannelAnalyticsDaily row in db.ChannelAnalyticsDailies
        )
            row.TotalMessages = 999999;
        foreach (
            NomNomzBot.Domain.Analytics.Entities.MessageActivityDaily row in db.MessageActivityDailies
        )
            row.MessageCount = 999999;
        foreach (
            NomNomzBot.Domain.Analytics.Entities.ViewerEngagementDaily row in db.ViewerEngagementDailies
        )
            row.MessageCount = 999999;
        foreach (NomNomzBot.Domain.Analytics.Entities.ViewerProfile row in db.ViewerProfiles)
            row.TotalMessages = 999999;
        foreach (NomNomzBot.Domain.Analytics.Entities.WatchSession row in db.WatchSessions)
            row.DurationSeconds = 999999;
        await db.SaveChangesAsync();
    }

    // ── event factories ──
    private static DomainEventBase Chat(string id, string login, string display, DateTime at) =>
        new NomNomzBot.Domain.Chat.Events.ChatMessageReceivedEvent
        {
            BroadcasterId = Channel,
            OccurredAt = new DateTimeOffset(at, TimeSpan.Zero),
            MessageId = Guid.NewGuid().ToString(),
            TwitchBroadcasterId = "39863651",
            UserId = id,
            UserLogin = login,
            UserDisplayName = display,
            Message = "hello",
            Fragments = [],
            Badges = [],
            IsSubscriber = false,
            IsVip = false,
            IsModerator = false,
            IsBroadcaster = false,
        };

    private static DomainEventBase Command(string id, string login, DateTime at) =>
        new NomNomzBot.Domain.Commands.Events.CommandExecutedEvent
        {
            BroadcasterId = Channel,
            OccurredAt = new DateTimeOffset(at, TimeSpan.Zero),
            CommandName = "!hello",
            UserId = id,
            Username = login,
            UserDisplayName = login,
            Succeeded = true,
        };

    private static DomainEventBase Reward(string id, string display, DateTime at) =>
        new RewardRedeemedEvent
        {
            BroadcasterId = Channel,
            OccurredAt = new DateTimeOffset(at, TimeSpan.Zero),
            RewardId = "reward-1",
            RewardTitle = "Hydrate",
            RedemptionId = Guid.NewGuid().ToString(),
            UserId = id,
            UserDisplayName = display,
            Cost = 100,
            UserInput = null,
        };

    private sealed record ReadModelSnapshot(
        IReadOnlyList<string> ChannelDaily,
        IReadOnlyList<string> MessageDaily,
        IReadOnlyList<string> Engagement,
        IReadOnlyList<string> Profiles,
        IReadOnlyList<string> Sessions,
        IReadOnlyList<string> Viewers
    )
    {
        // The spread must actually populate every projection, otherwise the rebuild equality is vacuous.
        public void AssertNonTrivial()
        {
            ChannelDaily.Should().NotBeEmpty("the channel-daily projection folded the spread");
            MessageDaily.Should().NotBeEmpty("the message-activity projection folded chat");
            Engagement.Should().NotBeEmpty("the engagement projection folded activity");
            Profiles.Should().HaveCount(2, "two distinct viewers chatted");
            Sessions.Should().NotBeEmpty("watch-sessions opened inside the live window");
            Viewers.Should().HaveCount(2, "each viewer was get-or-created as a User");
        }
    }
}

/// <summary>Adapts <see cref="ReadModelRebuildDbContext"/> to the <see cref="IUnitOfWork"/> the journal drives.</summary>
internal sealed class RebuildTestUnitOfWork : IUnitOfWork
{
    private readonly ReadModelRebuildDbContext _db;
    private Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? _transaction;

    public RebuildTestUnitOfWork(ReadModelRebuildDbContext db) => _db = db;

    public Task<int> SaveChangesAsync(CancellationToken ct = default) => _db.SaveChangesAsync(ct);

    public async Task BeginTransactionAsync(CancellationToken ct = default) =>
        _transaction = await _db.Database.BeginTransactionAsync(ct);

    public async Task CommitTransactionAsync(CancellationToken ct = default)
    {
        if (_transaction is not null)
        {
            await _transaction.CommitAsync(ct);
            await _transaction.DisposeAsync();
            _transaction = null;
        }
    }

    public async Task RollbackTransactionAsync(CancellationToken ct = default)
    {
        if (_transaction is not null)
        {
            await _transaction.RollbackAsync(ct);
            await _transaction.DisposeAsync();
            _transaction = null;
        }
    }
}
