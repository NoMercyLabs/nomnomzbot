// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Linq;
using FluentAssertions;
using Newtonsoft.Json;
using NomNomzBot.Application.Abstractions.Auth;
using NomNomzBot.Application.Contracts.EventStore;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Domain.Analytics.Entities;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Infrastructure.Analytics;
using NomNomzBot.Infrastructure.Identity;
using NomNomzBot.Infrastructure.Tests.Identity;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Analytics;

/// <summary>
/// Proves the viewer-profile projection (analytics.md §3.1, M.1): folding chat get-or-creates the viewer as a
/// (non-setup) User — the owner's identity model — then upserts the per-viewer profile (message totals, seen
/// timestamps, snapshots); distinct viewers get distinct profiles; a reset zeroes the aggregates for replay.
/// </summary>
public sealed class ViewerProfileProjectionTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-000000002001");
    private static readonly DateTime Now = new(2026, 6, 22, 12, 0, 0, DateTimeKind.Utc);
    private static int _seq;

    private static (ViewerProfileProjection Sut, AuthDbContext Db) Build()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        ICurrentUserService currentUser = Substitute.For<ICurrentUserService>();
        IUserService userService = new UserService(db, currentUser);
        return (new ViewerProfileProjection(db, new ViewerResolver(db, userService)), db);
    }

    private static EventRecord Chat(
        string twitchId,
        string login,
        string display,
        bool isSubscriber = false
    ) =>
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
                    UserLogin = login,
                    UserDisplayName = display,
                    IsSubscriber = isSubscriber,
                }
            ),
            false,
            null,
            null,
            null,
            null,
            null,
            "{}",
            Now,
            Now
        );

    [Fact]
    public async Task Creates_the_viewer_as_a_user_and_counts_their_messages()
    {
        (ViewerProfileProjection sut, AuthDbContext db) = Build();

        await sut.ApplyAsync(Chat("t1", "cooluser", "CoolUser", isSubscriber: true));
        await sut.ApplyAsync(Chat("t1", "cooluser", "CoolUser"));
        await sut.ApplyAsync(Chat("t1", "cooluser", "CoolUser"));

        User viewer = db.Users.Single(u => u.TwitchUserId == "t1");
        ViewerProfile profile = db.ViewerProfiles.Single();
        profile.ViewerUserId.Should().Be(viewer.Id); // the viewer IS a User
        profile.ViewerTwitchUserId.Should().Be("t1");
        profile.DisplayNameSnapshot.Should().Be("CoolUser");
        profile.TotalMessages.Should().Be(3);
        profile.FirstSeenAt.Should().Be(Now);
        profile.LastSeenAt.Should().Be(Now);
    }

    [Fact]
    public async Task Distinct_viewers_get_distinct_profiles()
    {
        (ViewerProfileProjection sut, AuthDbContext db) = Build();

        await sut.ApplyAsync(Chat("t1", "one", "One"));
        await sut.ApplyAsync(Chat("t2", "two", "Two"));

        db.ViewerProfiles.Should().HaveCount(2);
        db.Users.Count(u => u.TwitchUserId == "t1" || u.TwitchUserId == "t2").Should().Be(2);
    }

    [Fact]
    public async Task Reset_zeroes_the_aggregates_for_replay()
    {
        (ViewerProfileProjection sut, AuthDbContext db) = Build();
        await sut.ApplyAsync(Chat("t1", "one", "One"));

        await sut.ResetAsync(Channel);

        ViewerProfile profile = db.ViewerProfiles.Single();
        profile.TotalMessages.Should().Be(0);
        profile.LastSeenAt.Should().BeNull();
    }
}
