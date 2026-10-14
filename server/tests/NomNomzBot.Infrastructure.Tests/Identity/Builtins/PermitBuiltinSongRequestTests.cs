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
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Commands.Builtin;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Authorization;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Application.Music.Services;
using NomNomzBot.Infrastructure.Identity.Builtins;
using NomNomzBot.Infrastructure.Tests.Chat;
using NomNomzBot.Infrastructure.Tests.Commands.Builtins;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Identity.Builtins;

/// <summary>
/// <c>!permit</c> sent as a chat reply, with no arguments, admits the viewer's held song request: the reply
/// target is the viewer's own <c>!sr</c> line, or the bot's refusal line (whose thread starts at the viewer).
/// Only a moderator or the broadcaster can do it.
/// </summary>
public sealed class PermitBuiltinSongRequestTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-0000000000c1");
    private const int ModeratorLevel = 10;

    private static readonly MusicTrack Admitted = new(
        "spotify:track:ghost",
        "Ghost Love Score",
        "Nightwish",
        "Once",
        null,
        600_000,
        "spotify"
    );

    private static (PermitBuiltin Permit, IMusicService Music) Build()
    {
        IMusicService music = Substitute.For<IMusicService>();
        music
            .AdmitPermittedRequestAsync(
                Channel.ToString(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Failure<MusicTrack>("none", "NO_PENDING_REQUEST"));
        music
            .AdmitPermittedRequestAsync(Channel.ToString(), "finwo_", Arg.Any<CancellationToken>())
            .Returns(Result.Success(Admitted));

        PermitBuiltin permit = new(
            Substitute.For<IPermitService>(),
            Substitute.For<IUserService>(),
            Substitute.For<IRoleResolver>(),
            Substitute.For<ITwitchUsersApi>(),
            new FakeTimeProvider(),
            TestBuiltinComposer.Create(),
            TestMentionTargets.LoginsOnly(),
            music
        );
        return (permit, music);
    }

    private static BuiltinCommandContext Reply(
        int roleLevel,
        string parentUser,
        string? threadUser = null
    ) =>
        new()
        {
            BroadcasterId = Channel,
            TriggeringUserId = "tw-mod",
            TriggeringUserLogin = "modlogin",
            TriggeringUserDisplayName = "ModName",
            RoleLevel = roleLevel,
            Args = string.Empty,
            ReplyParentMessageId = "parent-1",
            ReplyParentUserName = parentUser,
            ReplyThreadUserName = threadUser,
        };

    [Fact]
    public async Task A_moderator_reply_to_the_viewers_sr_line_admits_that_viewers_request()
    {
        (PermitBuiltin permit, IMusicService music) = Build();

        Result<string> reply = await permit.ExecuteAsync(Reply(ModeratorLevel, "finwo_"));

        reply.IsSuccess.Should().BeTrue();
        reply.Value.Should().Contain("Ghost Love Score").And.Contain("finwo_");
        await music
            .Received(1)
            .AdmitPermittedRequestAsync(Channel.ToString(), "finwo_", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_moderator_reply_to_the_bots_refusal_admits_the_viewer_who_started_the_thread()
    {
        (PermitBuiltin permit, IMusicService music) = Build();

        Result<string> reply = await permit.ExecuteAsync(
            Reply(ModeratorLevel, parentUser: "NomNomzBot", threadUser: "finwo_")
        );

        reply.Value.Should().Contain("Ghost Love Score").And.Contain("finwo_");
        await music
            .Received(1)
            .AdmitPermittedRequestAsync(Channel.ToString(), "finwo_", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_viewer_replying_with_permit_admits_nothing()
    {
        (PermitBuiltin permit, IMusicService music) = Build();

        await permit.ExecuteAsync(Reply(roleLevel: 0, parentUser: "finwo_"));

        await music.DidNotReceiveWithAnyArgs().AdmitPermittedRequestAsync(default!, default!);
    }

    [Fact]
    public async Task A_reply_with_nothing_held_for_the_viewer_says_so_and_queues_nothing()
    {
        (PermitBuiltin permit, IMusicService music) = Build();

        Result<string> reply = await permit.ExecuteAsync(Reply(ModeratorLevel, "someone_else"));

        reply.IsSuccess.Should().BeTrue();
        reply.Value.Should().NotContain("Ghost Love Score");
        reply.Value.Should().Contain("someone_else");
    }

    [Fact]
    public async Task A_permit_with_no_arguments_that_is_not_a_reply_still_shows_the_usage()
    {
        (PermitBuiltin permit, IMusicService music) = Build();

        Result<string> reply = await permit.ExecuteAsync(
            new()
            {
                BroadcasterId = Channel,
                TriggeringUserId = "tw-mod",
                TriggeringUserDisplayName = "ModName",
                RoleLevel = ModeratorLevel,
            }
        );

        reply.Value.Should().Contain("!permit @user");
        await music.DidNotReceiveWithAnyArgs().AdmitPermittedRequestAsync(default!, default!);
    }
}
