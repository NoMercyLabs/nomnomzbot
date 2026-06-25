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
using Microsoft.Extensions.DependencyInjection;
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
/// Proves the viewer-engagement-daily projection (analytics.md §3.1, M.7): chat / command / reward events for one
/// viewer (each naming the viewer differently — UserLogin, Username, UserDisplayName) fold into a single
/// (viewer, day) row tallying messages / commands / redemptions; a reset clears the rollup for replay.
/// </summary>
public sealed class ViewerEngagementDailyProjectionTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-000000005001");
    private static readonly DateTime Day = new(2026, 6, 22, 9, 0, 0, DateTimeKind.Utc);
    private static int _seq;

    private static (ViewerEngagementDailyProjection Sut, AuthDbContext Db) Build()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        ICurrentUserService currentUser = Substitute.For<ICurrentUserService>();
        IUserService userService = new UserService(
            db,
            currentUser,
            Substitute.For<IServiceScopeFactory>()
        );
        return (new ViewerEngagementDailyProjection(db, new ViewerResolver(db, userService)), db);
    }

    private static EventRecord Event(string type, object payload, DateTime at) =>
        new(
            ++_seq,
            Guid.NewGuid(),
            Channel,
            _seq,
            type,
            1,
            "domain",
            JsonConvert.SerializeObject(payload),
            false,
            null,
            null,
            null,
            null,
            null,
            "{}",
            at,
            at
        );

    [Fact]
    public async Task Folds_messages_commands_and_redemptions_into_one_viewer_day_row()
    {
        (ViewerEngagementDailyProjection sut, AuthDbContext db) = Build();

        await sut.ApplyAsync(
            Event(
                "ChatMessageReceivedEvent",
                new
                {
                    UserId = "t1",
                    UserLogin = "t1u",
                    UserDisplayName = "T1",
                },
                Day
            )
        );
        await sut.ApplyAsync(
            Event(
                "ChatMessageReceivedEvent",
                new
                {
                    UserId = "t1",
                    UserLogin = "t1u",
                    UserDisplayName = "T1",
                },
                Day.AddMinutes(1)
            )
        );
        await sut.ApplyAsync(
            Event(
                "CommandExecutedEvent",
                new { UserId = "t1", Username = "t1u" },
                Day.AddMinutes(2)
            )
        );
        await sut.ApplyAsync(
            Event(
                "RewardRedeemedEvent",
                new { UserId = "t1", UserDisplayName = "T1" },
                Day.AddMinutes(3)
            )
        );

        ViewerEngagementDaily row = db.ViewerEngagementDailies.Single();
        row.ActivityDate.Should().Be(new DateOnly(2026, 6, 22));
        row.MessageCount.Should().Be(2);
        row.CommandCount.Should().Be(1);
        row.RedemptionCount.Should().Be(1);
        row.ViewerProfileId.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Reset_clears_the_rollup_for_replay()
    {
        (ViewerEngagementDailyProjection sut, AuthDbContext db) = Build();
        await sut.ApplyAsync(
            Event("CommandExecutedEvent", new { UserId = "t1", Username = "t1u" }, Day)
        );
        db.ViewerEngagementDailies.Should().ContainSingle();

        await sut.ResetAsync(Channel);

        db.ViewerEngagementDailies.Should().BeEmpty();
    }
}
