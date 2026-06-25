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
using Newtonsoft.Json;
using NomNomzBot.Application.Abstractions.Auth;
using NomNomzBot.Application.Contracts.EventStore;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Domain.Analytics.Entities;
using NomNomzBot.Infrastructure.Analytics;
using NomNomzBot.Infrastructure.Identity;
using NomNomzBot.Infrastructure.Tests.Identity;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Analytics;

/// <summary>
/// Proves the message-activity-daily projection (analytics.md §3.1, M.4): chat folds into one upserted row per
/// (viewer, channel-local day) counting messages (resolving the viewer via the shared resolver), distinct days
/// get distinct rows, and a reset clears the rollup for replay.
/// </summary>
public sealed class MessageActivityDailyProjectionTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-000000003001");
    private static int _seq;

    private static (MessageActivityDailyProjection Sut, AuthDbContext Db) Build()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        ICurrentUserService currentUser = Substitute.For<ICurrentUserService>();
        IUserService userService = new UserService(db, currentUser);
        return (new MessageActivityDailyProjection(db, new ViewerResolver(db, userService)), db);
    }

    private static EventRecord Chat(string twitchId, DateTime occurredAt) =>
        new(
            ++_seq,
            Guid.NewGuid(),
            Channel,
            _seq,
            "ChatMessageReceivedEvent",
            1,
            "domain",
            JsonConvert.SerializeObject(
                new
                {
                    UserId = twitchId,
                    UserLogin = twitchId,
                    UserDisplayName = twitchId,
                }
            ),
            false,
            null,
            null,
            null,
            null,
            null,
            "{}",
            occurredAt,
            occurredAt
        );

    [Fact]
    public async Task Counts_daily_messages_per_viewer()
    {
        (MessageActivityDailyProjection sut, AuthDbContext db) = Build();
        DateTime day = new(2026, 6, 22, 9, 0, 0, DateTimeKind.Utc);

        await sut.ApplyAsync(Chat("t1", day));
        await sut.ApplyAsync(Chat("t1", day.AddHours(1)));
        await sut.ApplyAsync(Chat("t1", day.AddHours(2)));

        MessageActivityDaily row = db.MessageActivityDailies.Single();
        row.MessageCount.Should().Be(3);
        row.ActivityDate.Should().Be(new DateOnly(2026, 6, 22));
        row.ViewerProfileId.Should().NotBeEmpty();
        row.FirstMessageAt.Should().Be(day);
        row.LastMessageAt.Should().Be(day.AddHours(2));
    }

    [Fact]
    public async Task Distinct_days_get_distinct_rows()
    {
        (MessageActivityDailyProjection sut, AuthDbContext db) = Build();

        await sut.ApplyAsync(Chat("t1", new DateTime(2026, 6, 22, 9, 0, 0, DateTimeKind.Utc)));
        await sut.ApplyAsync(Chat("t1", new DateTime(2026, 6, 23, 9, 0, 0, DateTimeKind.Utc)));

        db.MessageActivityDailies.Should().HaveCount(2);
    }

    [Fact]
    public async Task Reset_clears_the_rollup_for_replay()
    {
        (MessageActivityDailyProjection sut, AuthDbContext db) = Build();
        await sut.ApplyAsync(Chat("t1", new DateTime(2026, 6, 22, 9, 0, 0, DateTimeKind.Utc)));
        db.MessageActivityDailies.Should().ContainSingle();

        await sut.ResetAsync(Channel);

        db.MessageActivityDailies.Should().BeEmpty();
    }
}
