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
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Application.Commands.Builtin;
using NomNomzBot.Application.Commands.Builtin.Personality;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Music.Services;
using NomNomzBot.Infrastructure.Commands.Builtins;
using NomNomzBot.Infrastructure.Tests.Commands.Builtins;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Music;

/// <summary>
/// <c>!pause</c> and <c>!resume</c> — the chat twins of the dashboard's pause/play buttons, gated exactly like
/// <c>!skip</c> (moderator or the music:queue:moderate grant). Every outcome answers in chat: a success, and a
/// reply per failure code, so a streamer whose Audio widget is closed hears why nothing happened.
/// </summary>
public sealed class PauseResumeBuiltinTests
{
    private static readonly Guid Broadcaster = Guid.Parse("0192a000-0000-7000-8000-0000000ac0a1");

    private const int ViewerLevel = 0;
    private const int ModeratorLevel = 10;

    private static BuiltinCommandContext Ctx(int roleLevel) =>
        new()
        {
            BroadcasterId = Broadcaster,
            TriggeringUserId = "twitch-42",
            TriggeringUserDisplayName = "Bamo",
            RoleLevel = roleLevel,
            Args = string.Empty,
        };

    private static IBuiltinResponseComposer FakeComposer(
        IChannelBuiltinReplyOverrides? channelReplies = null
    )
    {
        ITemplateResolver resolver = Substitute.For<ITemplateResolver>();
        resolver
            .ResolveAsync(
                Arg.Any<string>(),
                Arg.Any<IDictionary<string, string>>(),
                Arg.Any<Guid?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(call => Task.FromResult(call.ArgAt<string>(0)));
        return new BuiltinResponseComposer(
            resolver,
            NoPlatformBuiltinReplies.Instance,
            channelReplies ?? FakeChannelBuiltinReplies.None
        );
    }

    private static IMusicService MusicAnswering(Result pause, Result play)
    {
        IMusicService music = Substitute.For<IMusicService>();
        music.PauseAsync(Broadcaster.ToString(), Arg.Any<CancellationToken>()).Returns(pause);
        music.PlayAsync(Broadcaster.ToString(), Arg.Any<CancellationToken>()).Returns(play);
        return music;
    }

    private static PauseBuiltin Pause(IMusicService music, bool holdsGrant = false) =>
        new(music, FakeComposer(), MusicGateTestKit.Gate(holdsGrant));

    private static ResumeBuiltin Resume(IMusicService music, bool holdsGrant = false) =>
        new(music, FakeComposer(), MusicGateTestKit.Gate(holdsGrant));

    [Fact]
    public async Task A_moderator_pauses_the_music_and_chat_hears_it_paused()
    {
        IMusicService music = MusicAnswering(Result.Success(), Result.Success());

        Result<string> result = await Pause(music).ExecuteAsync(Ctx(ModeratorLevel));

        result.Value.Should().Be("Paused the music.");
        await music.Received(1).PauseAsync(Broadcaster.ToString(), Arg.Any<CancellationToken>());
        await music.DidNotReceiveWithAnyArgs().PlayAsync(default!);
    }

    [Fact]
    public async Task A_moderator_resumes_the_music_and_chat_hears_it_resumed()
    {
        IMusicService music = MusicAnswering(Result.Success(), Result.Success());

        Result<string> result = await Resume(music).ExecuteAsync(Ctx(ModeratorLevel));

        result.Value.Should().Be("Resumed the music.");
        await music.Received(1).PlayAsync(Broadcaster.ToString(), Arg.Any<CancellationToken>());
        await music.DidNotReceiveWithAnyArgs().PauseAsync(default!);
    }

    [Fact]
    public async Task A_grant_holder_pauses_like_a_moderator()
    {
        IMusicService music = MusicAnswering(Result.Success(), Result.Success());

        Result<string> result = await Pause(music, holdsGrant: true)
            .ExecuteAsync(Ctx(ViewerLevel));

        result.Value.Should().Be("Paused the music.");
        await music.Received(1).PauseAsync(Broadcaster.ToString(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_plain_viewer_gets_the_permission_reply_and_the_player_is_never_touched()
    {
        IMusicService music = MusicAnswering(Result.Success(), Result.Success());
        FakeChannelBuiltinReplies channel = new FakeChannelBuiltinReplies().Set(
            Broadcaster,
            BuiltinResponseSlots.SystemReplies.Key,
            BuiltinResponseSlots.SystemReplies.PermissionDenied,
            "Mods only, friend."
        );
        PauseBuiltin pause = new(
            music,
            FakeComposer(channel),
            MusicGateTestKit.Gate(holdsGrant: false)
        );
        ResumeBuiltin resume = new(
            music,
            FakeComposer(channel),
            MusicGateTestKit.Gate(holdsGrant: false)
        );

        (await pause.ExecuteAsync(Ctx(ViewerLevel))).Value.Should().Be("Mods only, friend.");
        (await resume.ExecuteAsync(Ctx(ViewerLevel))).Value.Should().Be("Mods only, friend.");
        await music.DidNotReceiveWithAnyArgs().PauseAsync(default!);
        await music.DidNotReceiveWithAnyArgs().PlayAsync(default!);
    }

    [Theory]
    [InlineData(
        "NO_ACTIVE_DEVICE",
        "The music player isn't open — add the Audio widget to OBS, or open Spotify."
    )]
    [InlineData("SERVICE_UNAVAILABLE", "No music service is set up for this channel.")]
    [InlineData("PREMIUM_REQUIRED", "The music service needs a Premium account for that.")]
    [InlineData("CAPABILITY_UNSUPPORTED", "Couldn't pause the music.")]
    [InlineData("MUSIC_AUTH_FAILED", "Couldn't pause the music.")]
    public async Task A_failed_pause_answers_with_the_reply_for_its_failure(
        string errorCode,
        string expectedReply
    )
    {
        IMusicService music = MusicAnswering(
            Result.Failure("internal provider detail 7731", errorCode),
            Result.Success()
        );

        Result<string> result = await Pause(music).ExecuteAsync(Ctx(ModeratorLevel));

        result.Value.Should().Be(expectedReply);
        result.Value.Should().NotContain("7731");
    }

    [Theory]
    [InlineData(
        "NO_ACTIVE_DEVICE",
        "The music player isn't open — add the Audio widget to OBS, or open Spotify."
    )]
    [InlineData("SERVICE_UNAVAILABLE", "No music service is set up for this channel.")]
    [InlineData("PREMIUM_REQUIRED", "The music service needs a Premium account for that.")]
    [InlineData("CAPABILITY_UNSUPPORTED", "Couldn't resume the music.")]
    public async Task A_failed_resume_answers_with_the_reply_for_its_failure(
        string errorCode,
        string expectedReply
    )
    {
        IMusicService music = MusicAnswering(
            Result.Success(),
            Result.Failure("internal provider detail 7731", errorCode)
        );

        Result<string> result = await Resume(music).ExecuteAsync(Ctx(ModeratorLevel));

        result.Value.Should().Be(expectedReply);
    }
}
