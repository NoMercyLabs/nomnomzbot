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
using Microsoft.Extensions.Time.Testing;
using Newtonsoft.Json.Linq;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.EventStore;
using NomNomzBot.Domain.Community.Events;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Moderation.Events;
using NomNomzBot.Domain.Platform;
using NomNomzBot.Domain.Rewards.Events;
using NomNomzBot.Domain.Stream.Events;
using NomNomzBot.Infrastructure.Analytics;
using NomNomzBot.Infrastructure.EventStore;
using NomNomzBot.Infrastructure.Tests.EventStore;

namespace NomNomzBot.Infrastructure.Tests.Analytics;

/// <summary>
/// Proves the channel-event-log projection folds the journal's channel facts into the <see cref="ChannelEvent"/> read
/// model (event-store §3.3, schema F.4) and that reset → replay rebuilds it identically and idempotently. A spread of
/// real domain events for two viewers is appended to a relational journal, folded once, and the resulting rows are
/// asserted on their <em>shape</em> — the channel type string, the tenant attribution, the idempotency key, and the
/// scrubbed <c>Data</c> fields — not merely that rows exist. The read model is then corrupted and rebuilt from
/// position 0; the rebuilt rows must equal the original snapshot, proving the log derives from <c>EventJournal</c>
/// alone. A second incremental fold over the same head applies nothing (idempotent upsert on <c>EventId</c>).
/// </summary>
public sealed class TwitchChannelEventLogProjectionTests
{
    private static readonly FakeTimeProvider Clock = new(
        new DateTimeOffset(2026, 6, 22, 20, 0, 0, TimeSpan.Zero)
    );
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-0000000abc0e");
    private static readonly Guid OtherChannel = Guid.Parse("0192a000-0000-7000-8000-0000000abc0f");
    private static readonly DateTime Live = new(2026, 6, 22, 20, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Folds_each_channel_fact_into_a_typed_scrubbed_row_keyed_by_event_id()
    {
        using ReadModelRebuildDatabase database = ReadModelRebuildDatabase.Open();
        await using ReadModelRebuildDbContext db = database.NewContext();
        EventJournalService journal = NewJournal(db);

        IReadOnlyList<DomainEventBase> spread = Spread();
        await AppendAsync(journal, spread);

        TwitchChannelEventLogProjection projection = NewProjection(db);
        ProjectionRunner runner = NewRunner(db, journal, projection);

        Result<long> applied = await runner.RunOnceAsync(projection.Name, Channel);
        applied.IsSuccess.Should().BeTrue(applied.ErrorMessage);
        applied.Value.Should().Be(spread.Count, "every appended fact is a surfaced channel event");

        List<ChannelEvent> rows = await db
            .ChannelEvents.AsNoTracking()
            .OrderBy(r => r.CreatedAt)
            .ToListAsync();

        // One row per fact, all attributed to this tenant, each keyed by its journal EventId so replay can upsert it.
        // The fold leaves UserId null (the set-based ChannelEventActorBackfill links it after the rebuild) and instead
        // snapshots the actor's Twitch id under a stable actorTwitchUserId key — including events whose actor is NOT
        // the generic "UserId" field (raid=FromUserId, gift=GifterUserId, ban=TargetUserId).
        rows.Should().HaveCount(spread.Count);
        rows.Should().OnlyContain(r => r.ChannelId == Channel);
        rows.Should().OnlyContain(r => r.UserId == null);
        rows.Select(r => r.Id).Should().BeEquivalentTo(spread.Select(e => e.EventId.ToString()));

        DataOf(rows, "channel.cheer")["actorTwitchUserId"]!.Value<string>().Should().Be("200");
        DataOf(rows, "channel.raid")["actorTwitchUserId"]!.Value<string>().Should().Be("300");
        DataOf(rows, "channel.subscription.gift")["actorTwitchUserId"]!
            .Value<string>()
            .Should()
            .Be("100");
        DataOf(rows, "channel.ban")["actorTwitchUserId"]!.Value<string>().Should().Be("400");

        rows.Select(r => r.Type)
            .Should()
            .BeEquivalentTo([
                "channel.follow",
                "channel.subscribe",
                "channel.subscription.gift",
                "channel.cheer",
                "channel.raid",
                "channel.channel_points_custom_reward_redemption.add",
                "channel.ban",
            ]);

        // The scrubbed Data carries the structured, renderable fields — and only those.
        JObject cheer = DataOf(rows, "channel.cheer");
        cheer["userId"]!.Value<string>().Should().Be("200");
        cheer["userDisplayName"]!.Value<string>().Should().Be("Bob");
        cheer["bits"]!.Value<int>().Should().Be(150);
        cheer.Should().NotContainKey("Message", "free-text is scrubbed per F.4");

        JObject raid = DataOf(rows, "channel.raid");
        raid["fromUserId"]!.Value<string>().Should().Be("300");
        raid["fromDisplayName"]!.Value<string>().Should().Be("Carol");
        raid["viewerCount"]!.Value<int>().Should().Be(42);

        JObject ban = DataOf(rows, "channel.ban");
        ban["targetUserId"]!.Value<string>().Should().Be("400");
        ban["moderatorUserId"]!.Value<string>().Should().Be("999");

        JObject gift = DataOf(rows, "channel.subscription.gift");
        gift["gifterUserId"]!.Value<string>().Should().Be("100");
        gift["giftCount"]!.Value<int>().Should().Be(2);
        gift["isAnonymous"]!.Value<bool>().Should().BeFalse();
    }

    [Fact]
    public async Task Reset_then_replay_rebuilds_the_log_identically_and_is_idempotent()
    {
        using ReadModelRebuildDatabase database = ReadModelRebuildDatabase.Open();
        await using ReadModelRebuildDbContext db = database.NewContext();
        EventJournalService journal = NewJournal(db);

        IReadOnlyList<DomainEventBase> spread = Spread();
        await AppendAsync(journal, spread);

        TwitchChannelEventLogProjection projection = NewProjection(db);
        ProjectionRunner runner = NewRunner(db, journal, projection);

        await runner.RunOnceAsync(projection.Name, Channel);
        List<string> original = await SnapshotAsync(db);
        original.Should().HaveCount(spread.Count);

        // A second incremental pass over the same head must apply nothing — the upsert is idempotent on EventId.
        Result<long> second = await runner.RunOnceAsync(projection.Name, Channel);
        second.IsSuccess.Should().BeTrue(second.ErrorMessage);
        second.Value.Should().Be(0, "the checkpoint is at the head; no new events to fold");
        (await db.ChannelEvents.CountAsync()).Should().Be(spread.Count, "no duplicate rows");

        // Corrupt every row, then rebuild from zero. Reset wipes the tenant's rows; replay re-derives them.
        foreach (ChannelEvent row in db.ChannelEvents)
        {
            row.Type = "corrupted";
            row.Data = "{}";
        }
        await db.SaveChangesAsync();

        Result<long> rebuilt = await runner.RebuildAsync(projection.Name, Channel);
        rebuilt.IsSuccess.Should().BeTrue(rebuilt.ErrorMessage);
        rebuilt.Value.Should().Be(spread.Count);

        List<string> rebuiltSnapshot = await SnapshotAsync(db);
        rebuiltSnapshot
            .Should()
            .BeEquivalentTo(
                original,
                "the channel event log must reconstruct identically from EventJournal alone"
            );
    }

    [Fact]
    public async Task Reset_is_tenant_scoped_and_never_touches_another_channels_rows()
    {
        using ReadModelRebuildDatabase database = ReadModelRebuildDatabase.Open();
        await using ReadModelRebuildDbContext db = database.NewContext();

        TwitchChannelEventLogProjection projection = NewProjection(db);

        // A foreign tenant's row that the reset must not delete.
        db.ChannelEvents.Add(
            new ChannelEvent
            {
                Id = Guid.NewGuid().ToString(),
                ChannelId = OtherChannel,
                Type = "channel.follow",
                Data = "{}",
                CreatedAt = Live,
                UpdatedAt = Live,
            }
        );
        // This tenant's row that the reset must delete.
        db.ChannelEvents.Add(
            new ChannelEvent
            {
                Id = Guid.NewGuid().ToString(),
                ChannelId = Channel,
                Type = "channel.cheer",
                Data = "{}",
                CreatedAt = Live,
                UpdatedAt = Live,
            }
        );
        await db.SaveChangesAsync();

        Result reset = await projection.ResetAsync(Channel);
        reset.IsSuccess.Should().BeTrue(reset.ErrorMessage);

        List<ChannelEvent> remaining = await db.ChannelEvents.AsNoTracking().ToListAsync();
        remaining.Should().ContainSingle().Which.ChannelId.Should().Be(OtherChannel);
    }

    // ── helpers ──
    private static TwitchChannelEventLogProjection NewProjection(ReadModelRebuildDbContext db) =>
        new(db);

    private static EventJournalService NewJournal(ReadModelRebuildDbContext db) =>
        new(
            db,
            new TenantSequenceAllocator(db),
            new RebuildTestUnitOfWork(db),
            Clock,
            new PassthroughEventPayloadProtector()
        );

    private static ProjectionRunner NewRunner(
        ReadModelRebuildDbContext db,
        EventJournalService journal,
        IProjection projection
    ) => new([projection], journal, new EventUpcasterRegistry([]), db, Clock);

    private static async Task AppendAsync(
        EventJournalService journal,
        IReadOnlyList<DomainEventBase> events
    )
    {
        foreach (DomainEventBase @event in events)
        {
            AppendEventRequest request = new(
                EventId: @event.EventId,
                BroadcasterId: Channel,
                EventType: @event.GetType().Name,
                EventVersion: 1,
                Source: "import",
                PayloadJson: Newtonsoft.Json.JsonConvert.SerializeObject(@event),
                MetadataJson: "{}",
                OccurredAt: @event.OccurredAt.UtcDateTime
            );
            Result<EventRecord> appended = await journal.AppendAsync(request);
            appended.IsSuccess.Should().BeTrue(appended.ErrorMessage);
        }
    }

    private static async Task<List<string>> SnapshotAsync(ReadModelRebuildDbContext db) =>
        await db
            .ChannelEvents.AsNoTracking()
            .OrderBy(r => r.Id)
            .Select(r => $"{r.Id}|{r.ChannelId}|{r.UserId}|{r.Type}|{r.Data}|{r.CreatedAt:O}")
            .ToListAsync();

    private static JObject DataOf(IEnumerable<ChannelEvent> rows, string type) =>
        JObject.Parse(rows.Single(r => r.Type == type).Data!);

    private static IReadOnlyList<DomainEventBase> Spread() =>
        [
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
                OccurredAt = new DateTimeOffset(Live.AddSeconds(10), TimeSpan.Zero),
                UserId = "200",
                UserDisplayName = "Bob",
                Tier = "1000",
            },
            new GiftSubscriptionEvent
            {
                BroadcasterId = Channel,
                OccurredAt = new DateTimeOffset(Live.AddSeconds(20), TimeSpan.Zero),
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
                OccurredAt = new DateTimeOffset(Live.AddSeconds(30), TimeSpan.Zero),
                UserId = "200",
                UserDisplayName = "Bob",
                Bits = 150,
                Message = "Cheer150 nice stream",
                IsAnonymous = false,
            },
            new RaidEvent
            {
                BroadcasterId = Channel,
                OccurredAt = new DateTimeOffset(Live.AddSeconds(40), TimeSpan.Zero),
                FromUserId = "300",
                FromDisplayName = "Carol",
                FromLogin = "carol",
                ViewerCount = 42,
            },
            new RewardRedeemedEvent
            {
                BroadcasterId = Channel,
                OccurredAt = new DateTimeOffset(Live.AddSeconds(50), TimeSpan.Zero),
                RewardId = "reward-1",
                RewardTitle = "Hydrate",
                RedemptionId = "redemption-1",
                UserId = "100",
                UserDisplayName = "Alice",
                Cost = 100,
                UserInput = null,
            },
            new UserBannedEvent
            {
                BroadcasterId = Channel,
                OccurredAt = new DateTimeOffset(Live.AddSeconds(60), TimeSpan.Zero),
                TargetUserId = "400",
                TargetDisplayName = "Mallory",
                ModeratorUserId = "999",
                Reason = "spam",
            },
        ];
}
