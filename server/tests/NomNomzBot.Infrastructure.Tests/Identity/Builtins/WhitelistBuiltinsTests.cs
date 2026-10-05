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
using NomNomzBot.Application.Commands.Builtin;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Authorization;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Infrastructure.Identity;
using NomNomzBot.Infrastructure.Identity.Builtins;
using NomNomzBot.Infrastructure.Tests.Commands.Builtins;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Identity.Builtins;

/// <summary>
/// Proves <c>!whitelist</c> / <c>!unwhitelist</c> against the REAL standing service and role resolver: the
/// override changes the effective level the command gate reads, survives every sync path, and is removed again
/// by <c>!unwhitelist</c>; every reply is the old bot's exact text.
/// </summary>
public sealed class WhitelistBuiltinsTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-0000000000c1");
    private static readonly Guid TargetGuid = Guid.Parse("0192a000-0000-7000-8000-0000000000c3");
    private static readonly DateTimeOffset Now = new(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);

    private sealed record Harness(
        WhitelistBuiltin Whitelist,
        UnwhitelistBuiltin Unwhitelist,
        CommunityStandingService Standings,
        RoleResolver Roles,
        AuthDbContext Db
    );

    private static Harness Build()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        FakeTimeProvider clock = new(Now);
        CommunityStandingService standings = new(db, new RecordingEventBus(), clock);
        RoleResolver roles = new(db, clock, Substitute.For<IActAsMembershipOverlay>());

        IUserService users = Substitute.For<IUserService>();
        users
            .GetOrCreateAsync(
                "tw-target",
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                Result.Success(
                    new UserDto(
                        Id: TargetGuid.ToString(),
                        Username: "someone",
                        DisplayName: "Someone",
                        ProfileImageUrl: null,
                        Email: null,
                        CreatedAt: DateTime.UnixEpoch,
                        LastLoginAt: DateTime.UnixEpoch
                    )
                )
            );

        ITwitchUsersApi twitchUsers = Substitute.For<ITwitchUsersApi>();
        twitchUsers
            .GetUsersByLoginsAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(call =>
                call.ArgAt<IReadOnlyList<string>>(0).Contains("someone")
                    ? Result.Success<IReadOnlyList<TwitchUser>>([
                        new("tw-target", "someone", "Someone", "", "", "", "", "", 0, Now),
                    ])
                    : Result.Success<IReadOnlyList<TwitchUser>>([])
            );

        IBuiltinResponseComposer composer = TestBuiltinComposer.Create();
        return new(
            new(standings, users, twitchUsers, composer),
            new(standings, users, twitchUsers, composer),
            standings,
            roles,
            db
        );
    }

    private static BuiltinCommandContext Context(string args, int roleLevel = 40) =>
        new()
        {
            BroadcasterId = Channel,
            TriggeringUserId = "tw-owner",
            TriggeringUserLogin = "owner",
            TriggeringUserDisplayName = "Owner",
            RoleLevel = roleLevel,
            Args = args,
        };

    private static async Task<int> LevelAsync(Harness h) =>
        (await h.Roles.ResolveEffectiveLevelAsync(TargetGuid, Channel)).Value;

    private static CommunityStandingSnapshot Snapshot(
        CommunityStanding? reported,
        bool authoritative = true
    ) =>
        new(
            reported is { } standing ? [new(TargetGuid, standing, null)] : [],
            authoritative,
            authoritative
        );

    [Fact]
    public async Task Whitelist_subscriber_raises_the_effective_level_and_survives_every_sync_path()
    {
        Harness h = Build();

        Result<string> reply = await h.Whitelist.ExecuteAsync(Context("@Someone subscriber"));

        reply.Value.Should().Be("@Someone has been granted subscriber level access.");
        ChannelCommunityStanding row = await h.Db.ChannelCommunityStandings.SingleAsync();
        row.Source.Should().Be(StandingSource.Manual);
        row.Standing.Should().Be(CommunityStanding.Subscriber);
        row.LevelValue.Should().Be(CommunityStanding.Subscriber.ToLevel());
        (await LevelAsync(h)).Should().Be(2);

        // A chat-tag read that says "not a subscriber" and a full authoritative Twitch read that does not list
        // the viewer must both leave the override alone.
        await h.Standings.UpsertStandingAsync(
            Channel,
            TargetGuid,
            CommunityStanding.Everyone,
            StandingSource.ChatTags,
            null
        );
        await h.Standings.ReconcileTwitchStandingsAsync(Channel, Snapshot(null));
        await h.Standings.ReconcileTwitchStandingsAsync(Channel, Snapshot(CommunityStanding.Vip));

        ChannelCommunityStanding after = await h.Db.ChannelCommunityStandings.SingleAsync();
        after.Source.Should().Be(StandingSource.Manual);
        after.Standing.Should().Be(CommunityStanding.Subscriber);
        (await LevelAsync(h)).Should().Be(2);
    }

    [Theory]
    [InlineData("vip", CommunityStanding.Vip, 4)]
    [InlineData("MODERATOR", CommunityStanding.Moderator, 10)]
    public async Task Whitelist_vip_and_moderator_write_their_standing(
        string level,
        CommunityStanding expected,
        int expectedLevel
    )
    {
        Harness h = Build();

        Result<string> reply = await h.Whitelist.ExecuteAsync(Context($"@someone {level}"));

        reply
            .Value.Should()
            .Be($"@Someone has been granted {level.ToLowerInvariant()} level access.");
        (await h.Db.ChannelCommunityStandings.SingleAsync()).Standing.Should().Be(expected);
        (await LevelAsync(h)).Should().Be(expectedLevel);
    }

    [Fact]
    public async Task Unwhitelist_removes_the_override_and_the_synced_level_returns_on_the_next_read()
    {
        Harness h = Build();
        await h.Whitelist.ExecuteAsync(Context("@someone vip"));

        Result<string> reply = await h.Unwhitelist.ExecuteAsync(Context("@someone"));

        reply.Value.Should().Be("@Someone's permission override has been removed.");
        (await h.Db.ChannelCommunityStandings.CountAsync()).Should().Be(0);
        (await LevelAsync(h)).Should().Be(0);

        await h.Standings.ReconcileTwitchStandingsAsync(
            Channel,
            Snapshot(CommunityStanding.Subscriber)
        );
        (await LevelAsync(h)).Should().Be(2);
    }

    [Fact]
    public async Task Unwhitelist_lets_the_next_chat_message_bring_the_synced_standing_back()
    {
        Harness h = Build();
        await h.Whitelist.ExecuteAsync(Context("@someone moderator"));
        await h.Unwhitelist.ExecuteAsync(Context("@someone"));

        await h.Standings.UpsertStandingAsync(
            Channel,
            TargetGuid,
            CommunityStanding.Subscriber,
            StandingSource.ChatTags,
            null
        );

        ChannelCommunityStanding row = await h.Db.ChannelCommunityStandings.SingleAsync();
        row.Source.Should().Be(StandingSource.ChatTags);
        row.Standing.Should().Be(CommunityStanding.Subscriber);
        (await LevelAsync(h)).Should().Be(2);
    }

    [Fact]
    public async Task Unwhitelist_without_an_override_says_so_and_keeps_the_synced_row()
    {
        Harness h = Build();
        await h.Standings.ReconcileTwitchStandingsAsync(
            Channel,
            Snapshot(CommunityStanding.Subscriber)
        );

        Result<string> reply = await h.Unwhitelist.ExecuteAsync(Context("@someone"));

        reply.Value.Should().Be("@Someone had no permission override.");
        (await h.Db.ChannelCommunityStandings.SingleAsync())
            .Source.Should()
            .Be(StandingSource.HelixSeed);
        (await LevelAsync(h)).Should().Be(2);
    }

    [Theory]
    [InlineData("whitelist", "Only the broadcaster can use !whitelist.")]
    [InlineData("unwhitelist", "Only the broadcaster can use !unwhitelist.")]
    public async Task A_non_broadcaster_is_refused_and_nothing_is_written(
        string command,
        string expected
    )
    {
        Harness h = Build();
        BuiltinCommandContext context = Context("@someone vip", roleLevel: 10);

        Result<string> reply =
            command == "whitelist"
                ? await h.Whitelist.ExecuteAsync(context)
                : await h.Unwhitelist.ExecuteAsync(context);

        reply.Value.Should().Be(expected);
        (await h.Db.ChannelCommunityStandings.CountAsync()).Should().Be(0);
    }

    [Theory]
    [InlineData("whitelist")]
    [InlineData("unwhitelist")]
    public async Task An_unknown_twitch_user_gets_the_legacy_reply_and_nothing_is_written(
        string command
    )
    {
        Harness h = Build();

        Result<string> reply =
            command == "whitelist"
                ? await h.Whitelist.ExecuteAsync(Context("@ghost vip"))
                : await h.Unwhitelist.ExecuteAsync(Context("@ghost"));

        reply.Value.Should().Be("User \"ghost\" not found on Twitch.");
        (await h.Db.ChannelCommunityStandings.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Whitelist_with_a_bad_level_or_no_arguments_speaks_the_legacy_text()
    {
        Harness h = Build();

        (await h.Whitelist.ExecuteAsync(Context("@someone admin")))
            .Value.Should()
            .Be("Invalid level \"admin\". Valid levels: subscriber, vip, moderator");
        (await h.Whitelist.ExecuteAsync(Context("@someone")))
            .Value.Should()
            .Be("Usage: !whitelist @user subscriber/vip/moderator");
        (await h.Unwhitelist.ExecuteAsync(Context("")))
            .Value.Should()
            .Be("Usage: !unwhitelist @user");
        (await h.Db.ChannelCommunityStandings.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Whitelist_replaces_a_synced_row_with_the_override()
    {
        Harness h = Build();
        await h.Standings.ReconcileTwitchStandingsAsync(
            Channel,
            Snapshot(CommunityStanding.Subscriber)
        );

        await h.Whitelist.ExecuteAsync(Context("@someone vip"));

        ChannelCommunityStanding row = await h.Db.ChannelCommunityStandings.SingleAsync();
        row.Source.Should().Be(StandingSource.Manual);
        row.Standing.Should().Be(CommunityStanding.Vip);
    }
}
