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
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Contracts.Analytics;
using NomNomzBot.Application.Contracts.EventStore;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Domain.Analytics.Entities;
using NomNomzBot.Infrastructure.Analytics;
using NomNomzBot.Infrastructure.Identity;
using NomNomzBot.Infrastructure.Tests.Identity;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Analytics;

/// <summary>
/// Proves the watch-session presence derivation (analytics.md §1.1, M.2): activity inside a live window opens then
/// extends a per-(viewer, stream) session; presence confirms only once an activity lands ≥60s after the start;
/// activity outside any live window opens no session; a reset clears the rollup for replay.
/// </summary>
public sealed class WatchSessionProjectionTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-000000006001");
    private static readonly DateTime T0 = new(2026, 6, 22, 20, 0, 0, DateTimeKind.Utc);
    private static int _seq;

    private static (WatchSessionProjection Sut, AuthDbContext Db) Build(
        string? streamId = "stream1"
    )
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        ICurrentUserService currentUser = Substitute.For<ICurrentUserService>();
        ServiceCollection services = new();
        services.AddSingleton<IApplicationDbContext>(db);
        ServiceProvider provider = services.BuildServiceProvider();
        IServiceScopeFactory scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();
        IUserService userService = new UserService(db, currentUser, scopeFactory);
        ILiveWindowResolver live = Substitute.For<ILiveWindowResolver>();
        live.GetCoveringStreamIdAsync(
                Arg.Any<Guid>(),
                Arg.Any<DateTime>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(streamId);
        return (new WatchSessionProjection(db, new ViewerResolver(db, userService), live), db);
    }

    private static EventRecord Chat(string twitchId, DateTime at) =>
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
                    UserLogin = twitchId + "u",
                    UserDisplayName = twitchId,
                }
            ),
            false,
            null,
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
    public async Task Opens_and_extends_a_session_and_confirms_presence_after_60s()
    {
        (WatchSessionProjection sut, AuthDbContext db) = Build("stream1");

        await sut.ApplyAsync(Chat("t1", T0));
        await sut.ApplyAsync(Chat("t1", T0.AddSeconds(90)));

        WatchSession session = db.WatchSessions.Single();
        session.StreamId.Should().Be("stream1");
        session.StartedAt.Should().Be(T0);
        session.EndedAt.Should().Be(T0.AddSeconds(90));
        session.DurationSeconds.Should().Be(90);
        session.MessageCountInSession.Should().Be(2);
        session.PresenceConfirmed.Should().BeTrue();
    }

    [Fact]
    public async Task Does_not_confirm_presence_for_a_quick_burst()
    {
        (WatchSessionProjection sut, AuthDbContext db) = Build("stream1");

        await sut.ApplyAsync(Chat("t1", T0));
        await sut.ApplyAsync(Chat("t1", T0.AddSeconds(10)));

        WatchSession session = db.WatchSessions.Single();
        session.MessageCountInSession.Should().Be(2);
        session.PresenceConfirmed.Should().BeFalse();
    }

    [Fact]
    public async Task Opens_no_session_outside_a_live_window()
    {
        (WatchSessionProjection sut, AuthDbContext db) = Build(streamId: null);

        await sut.ApplyAsync(Chat("t1", T0));

        db.WatchSessions.Should().BeEmpty();
    }

    [Fact]
    public async Task Reset_clears_the_sessions_for_replay()
    {
        (WatchSessionProjection sut, AuthDbContext db) = Build("stream1");
        await sut.ApplyAsync(Chat("t1", T0));
        db.WatchSessions.Should().ContainSingle();

        await sut.ResetAsync(Channel);

        db.WatchSessions.Should().BeEmpty();
    }
}
