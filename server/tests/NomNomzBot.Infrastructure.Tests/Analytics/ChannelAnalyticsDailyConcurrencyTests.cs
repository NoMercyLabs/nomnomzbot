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
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Analytics;
using NomNomzBot.Application.Contracts.EventStore;
using NomNomzBot.Domain.Analytics.Entities;
using NomNomzBot.Infrastructure.Analytics;
using NomNomzBot.Infrastructure.Tests.Identity;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Analytics;

/// <summary>
/// Proves S004d: <c>ChannelAnalyticsDailyProjection.ApplyAsync</c> no longer read-modify-writes a stale
/// (BroadcasterId, ActivityDate) daily row. Real-world trigger — <c>EventStoreController.Replay</c> /
/// <c>RebuildProjections</c> call <see cref="IProjection"/> directly with no run-once lease
/// (EventStoreProjectionDriver.cs / EventStoreController.cs:115-171), so a manual rebuild can race the
/// driver's own 15s tick over the SAME broadcaster+day. Before the fix, <c>GetOrCreateAsync</c> would
/// unconditionally INSERT whenever it didn't see an existing row, so two concurrent first-events for the
/// SAME day threw an Npgsql/SQLite unique-constraint violation instead of upserting — and even once a row
/// existed, mutating a tracked in-memory copy then calling SaveChangesAsync lost whichever writer's
/// SaveChanges committed second. Each task below opens its OWN connection against a SHARED SQLite database
/// (the harness's named shared-cache store, real unique-index enforcement), so operations genuinely
/// interleave rather than serialize behind one C# await — the same race a rebuild vs. the driver depends on.
/// </summary>
public sealed class ChannelAnalyticsDailyConcurrencyTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-0000000000e1");
    private static readonly DateTime Day = new(2026, 6, 22, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Concurrent_first_folds_for_the_same_broadcaster_and_day_upsert_one_row_not_duplicate_it()
    {
        string dbName = $"analytics-race-{Guid.NewGuid():N}";
        const int concurrency = 8;

        Task[] tasks = Enumerable
            .Range(0, concurrency)
            .Select(i =>
                Task.Run(async () =>
                {
                    using AuthDbContext db = AuthTestBuilder.NewContext(dbName);
                    ILiveWindowResolver live = Substitute.For<ILiveWindowResolver>();
                    live.GetCoveringStreamIdAsync(
                            Arg.Any<Guid>(),
                            Arg.Any<DateTime>(),
                            Arg.Any<CancellationToken>()
                        )
                        .Returns((string?)null);
                    ChannelAnalyticsDailyProjection sut = new(db, live);

                    Result result = await sut.ApplyAsync(ChatEvent($"viewer-{i}"));
                    result.IsSuccess.Should().BeTrue();
                })
            )
            .ToArray();

        await Task.WhenAll(tasks);

        using AuthDbContext verify = AuthTestBuilder.NewContext(dbName);
        List<ChannelAnalyticsDaily> rows = verify
            .ChannelAnalyticsDailies.IgnoreQueryFilters()
            .Where(r => r.BroadcasterId == Channel)
            .ToList();

        rows.Should()
            .ContainSingle(
                "concurrent first-events for the SAME (broadcaster, day) must upsert one row instead of "
                    + "colliding on the unique index or silently duplicating it"
            );
        rows[0]
            .TotalMessages.Should()
            .Be(concurrency, "every concurrent chat message must be counted");
        rows[0]
            .UniqueChatters.Should()
            .Be(
                concurrency,
                "every concurrent chatter is distinct, none may be lost to a stale overwrite"
            );
    }

    [Fact]
    public async Task Concurrent_folds_against_an_already_existing_row_accumulate_every_counter_without_loss()
    {
        string dbName = $"analytics-race-existing-{Guid.NewGuid():N}";

        // Seed the row up front so every concurrent writer below is an UPDATE against an existing row —
        // the read-modify-write shape the old `row.TotalMessages++; SaveChangesAsync()` pattern lost updates
        // on, independent of the insert-race the other test proves.
        using (AuthDbContext seed = AuthTestBuilder.NewContext(dbName))
        {
            seed.ChannelAnalyticsDailies.Add(
                new() { BroadcasterId = Channel, ActivityDate = DateOnly.FromDateTime(Day) }
            );
            await seed.SaveChangesAsync();
        }

        const int concurrency = 20;
        Task[] tasks = Enumerable
            .Range(0, concurrency)
            .Select(i =>
                Task.Run(async () =>
                {
                    using AuthDbContext db = AuthTestBuilder.NewContext(dbName);
                    ILiveWindowResolver live = Substitute.For<ILiveWindowResolver>();
                    live.GetCoveringStreamIdAsync(
                            Arg.Any<Guid>(),
                            Arg.Any<DateTime>(),
                            Arg.Any<CancellationToken>()
                        )
                        .Returns((string?)null);
                    ChannelAnalyticsDailyProjection sut = new(db, live);

                    // Every writer folds a message for the SAME single viewer, so UniqueChatters must stay
                    // exactly 1 (only the flip counts) while TotalMessages accumulates every one of them.
                    Result result = await sut.ApplyAsync(ChatEvent("shared-viewer"));
                    result.IsSuccess.Should().BeTrue();
                })
            )
            .ToArray();

        await Task.WhenAll(tasks);

        using AuthDbContext verify = AuthTestBuilder.NewContext(dbName);
        ChannelAnalyticsDaily row = verify
            .ChannelAnalyticsDailies.IgnoreQueryFilters()
            .Single(r => r.BroadcasterId == Channel);

        row.TotalMessages.Should()
            .Be(concurrency, "a lost update would undercount — each concurrent fold must survive");
        row.UniqueChatters.Should()
            .Be(1, "the SAME viewer flips Chatted exactly once, never double-counted");
    }

    private static EventRecord ChatEvent(string viewerId) =>
        new(
            Random.Shared.NextInt64(),
            Guid.NewGuid(),
            Channel,
            0,
            "ChatMessageReceivedEvent",
            1,
            "domain",
            $"{{\"UserId\":\"{viewerId}\",\"UserLogin\":\"{viewerId}\",\"UserDisplayName\":\"{viewerId}\"}}",
            false,
            null,
            null,
            null,
            null,
            null,
            null,
            "{}",
            Day,
            Day
        );
}
