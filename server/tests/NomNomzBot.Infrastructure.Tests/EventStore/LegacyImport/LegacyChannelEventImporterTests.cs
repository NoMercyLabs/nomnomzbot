// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Runtime.CompilerServices;
using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Abstractions.Auth;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.EventStore;
using NomNomzBot.Infrastructure.EventStore;
using NomNomzBot.Infrastructure.EventStore.LegacyImport;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.EventStore.LegacyImport;

/// <summary>
/// Proves the legacy importer drives the real journal: a spread of legacy rows is mapped and appended, unimported
/// types are skipped, and a re-run imports nothing (idempotent on the derived EventId) so a replay rebuilds the
/// owner's history exactly once. Assertions are on the journal's resulting state — the rows actually present,
/// their EventType/Source/StreamPosition — and on the import summary counts, not on call counts.
/// </summary>
public sealed class LegacyChannelEventImporterTests
{
    private static readonly FakeTimeProvider Clock = new(new(2026, 6, 22, 12, 0, 0, TimeSpan.Zero));
    private static readonly Guid Tenant = Guid.Parse("0192a000-0000-7000-8000-00000000bb01");

    private static EventJournalService NewJournal(EventStoreTestDbContext db) =>
        new(
            db,
            new TenantSequenceAllocator(db),
            new EventStoreTestUnitOfWork(db),
            Clock,
            new PassthroughEventPayloadProtector(),
            Substitute.For<ICurrentUserService>()
        );

    private static LegacyChannelEventRow Row(string type, string data, string id) =>
        new(
            Id: id,
            ChannelId: "39863651",
            UserId: "42660213",
            Type: type,
            Data: data,
            CreatedAt: new(2025, 8, 14, 17, 0, 0, DateTimeKind.Utc)
        );

    private static IReadOnlyList<LegacyChannelEventRow> Spread() =>
        [
            Row(
                "channel.follow",
                """{"UserId":"100","UserName":"Alice","UserLogin":"alice","FollowedAt":"2025-08-01T18:00:00+00:00"}""",
                "f1"
            ),
            Row(
                "channel.subscribe",
                """{"UserId":"200","UserName":"Bob","Tier":"1000","IsGift":false}""",
                "s1"
            ),
            Row(
                "channel.cheer",
                """{"IsAnonymous":false,"UserId":"100","UserName":"Alice","Message":"Cheer100","Bits":100}""",
                "c1"
            ),
            // Noise the importer must skip — not one of the imported channel events. (channel.update IS mapped
            // now; the reward-DEFINITION update channel.points.custom.reward.update is the intentionally-skipped one.)
            Row("channel.points.custom.reward.update", "{}", "u1"),
            Row("channel.poll.progress", "{}", "p1"),
        ];

    [Fact]
    public async Task Imports_mapped_rows_into_the_journal_and_skips_noise()
    {
        using SqliteTestDatabase database = SqliteTestDatabase.Open();
        await using EventStoreTestDbContext db = database.NewContext();
        EventJournalService journal = NewJournal(db);
        LegacyChannelEventImporter importer = new(journal, new());

        Result<LegacyImportSummary> result = await importer.ImportAsync(
            Tenant,
            new InMemorySource(Spread())
        );

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        LegacyImportSummary summary = result.Value;
        summary.TotalRead.Should().Be(5);
        summary
            .Imported.Should()
            .Be(
                3,
                "follow + subscribe + cheer map; reward-definition update + poll.progress are noise"
            );
        summary.SkippedUnmapped.Should().Be(2);
        summary.SkippedDuplicate.Should().Be(0);

        // The journal now holds exactly the three mapped facts, in stream order, as Source="import".
        Result<IReadOnlyList<EventRecord>> stream = await journal.ReadStreamAsync(Tenant, 0, 100);
        stream.IsSuccess.Should().BeTrue(stream.ErrorMessage);
        stream
            .Value.Select(e => e.EventType)
            .Should()
            .BeEquivalentTo(["FollowEvent", "NewSubscriptionEvent", "CheerEvent"]);
        stream.Value.Should().OnlyContain(e => e.Source == "import");
        stream.Value.Select(e => e.StreamPosition).Should().BeEquivalentTo([1L, 2L, 3L]);
    }

    [Fact]
    public async Task A_re_run_imports_nothing_because_the_derived_EventId_is_stable()
    {
        using SqliteTestDatabase database = SqliteTestDatabase.Open();
        await using EventStoreTestDbContext db = database.NewContext();
        EventJournalService journal = NewJournal(db);
        LegacyChannelEventImporter importer = new(journal, new());

        await importer.ImportAsync(Tenant, new InMemorySource(Spread()));
        Result<LegacyImportSummary> second = await importer.ImportAsync(
            Tenant,
            new InMemorySource(Spread())
        );

        second.IsSuccess.Should().BeTrue(second.ErrorMessage);
        second
            .Value.Imported.Should()
            .Be(0, "every mapped row already exists by its stable EventId");
        second.Value.SkippedDuplicate.Should().Be(3);

        // The journal head did not advance on the second run — no duplicate history was written.
        Result<long> head = await journal.GetHeadPositionAsync(Tenant);
        head.Value.Should().Be(3);
    }

    /// <summary>
    /// The 2026-08-27 incident: the legacy source reads rows by CreatedAt (its capture time), but a redemption's
    /// real Twitch RedeemedAt can be EARLIER than a follow's capture time even though the follow was read first —
    /// batched writes and retries mean capture order and event-time order diverge. Replay walks the journal by
    /// StreamPosition, so if StreamPosition followed capture order, the redemption played back AFTER the follow
    /// that actually happened later, live, out of order. The importer must assign StreamPosition by the events'
    /// real OccurredAt, not by the order the source happened to yield them in.
    /// </summary>
    [Fact]
    public async Task Assigns_StreamPosition_by_real_event_time_even_when_the_source_read_order_disagrees()
    {
        using SqliteTestDatabase database = SqliteTestDatabase.Open();
        await using EventStoreTestDbContext db = database.NewContext();
        EventJournalService journal = NewJournal(db);
        LegacyChannelEventImporter importer = new(journal, new());

        // Read order: follow (captured Aug 14) then redemption (captured Aug 14, same row batch) — but the
        // redemption's REAL RedeemedAt (Aug 1) precedes the follow's REAL FollowedAt (Aug 10).
        LegacyChannelEventRow lateFollow = Row(
            "channel.follow",
            """{"UserId":"100","UserName":"Alice","UserLogin":"alice","FollowedAt":"2025-08-10T00:00:00+00:00"}""",
            "f1"
        );
        LegacyChannelEventRow earlyRedemption = Row(
            "channel.points.custom.reward.redemption.add",
            """{"Id":"r1","UserId":"200","UserName":"Bob","RedeemedAt":"2025-08-01T00:00:00+00:00","Reward":{"Id":"rw1","Title":"Hydrate","Cost":50}}""",
            "r1"
        );

        Result<LegacyImportSummary> result = await importer.ImportAsync(
            Tenant,
            new InMemorySource([lateFollow, earlyRedemption])
        );

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.Value.Imported.Should().Be(2);

        Result<IReadOnlyList<EventRecord>> stream = await journal.ReadStreamAsync(Tenant, 0, 100);
        stream.IsSuccess.Should().BeTrue(stream.ErrorMessage);
        // The redemption (real time Aug 1) gets StreamPosition 1; the follow (real time Aug 10) gets
        // StreamPosition 2 — chronological by OccurredAt, the opposite of the read order.
        stream
            .Value.Select(e => e.EventType)
            .Should()
            .Equal("RewardRedeemedEvent", "FollowEvent");
    }

    private sealed class InMemorySource : ILegacyChannelEventSource
    {
        private readonly IReadOnlyList<LegacyChannelEventRow> _rows;

        public InMemorySource(IReadOnlyList<LegacyChannelEventRow> rows) => _rows = rows;

        public async IAsyncEnumerable<LegacyChannelEventRow> ReadAllAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken = default
        )
        {
            foreach (LegacyChannelEventRow row in _rows)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return row;
                await Task.Yield();
            }
        }
    }
}
