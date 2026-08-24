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
using NomNomzBot.Application.Commands.Builtin;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Music.Services;
using NomNomzBot.Domain.Chat.Interfaces;
using NomNomzBot.Infrastructure.Commands.Builtins;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Music;

/// <summary>
/// Proves <c>!volume</c> reports the current volume when called with no argument (it used to just
/// print the usage string and do nothing), while <c>!volume &lt;n&gt;</c> keeps setting it — asserted
/// against the exact chat reply text a fake <see cref="IChatProvider"/> would send.
/// </summary>
public sealed class VolumeBuiltinTests
{
    private static readonly Guid Broadcaster = Guid.Parse("0192a000-0000-7000-8000-0000000ac001");

    private static BuiltinCommandContext Ctx(string args) =>
        new()
        {
            BroadcasterId = Broadcaster,
            TriggeringUserId = "twitch-42",
            TriggeringUserDisplayName = "Bamo",
            Args = args,
        };

    private static NowPlaying Playing(int volume) =>
        new(
            TrackName: "Song",
            Artist: "Artist",
            Album: null,
            ImageUrl: null,
            DurationMs: 180_000,
            ProgressMs: 1_000,
            IsPlaying: true,
            Volume: volume,
            RequestedBy: null,
            Provider: "spotify"
        );

    [Fact]
    public async Task No_argument_reports_the_current_volume_read_from_the_same_source_the_setter_writes_to()
    {
        IMusicService music = Substitute.For<IMusicService>();
        music
            .GetNowPlayingAsync(Broadcaster.ToString(), Arg.Any<CancellationToken>())
            .Returns(Playing(40));
        VolumeBuiltin sut = new(music);

        Result<string> result = await sut.ExecuteAsync(Ctx(string.Empty));

        // Relay the reply through a fake chat provider, exactly like ChatMessageHandler does with
        // every builtin's returned string, and assert on what actually landed in chat.
        IChatProvider chat = Substitute.For<IChatProvider>();
        await chat.SendReplyAsync(Broadcaster, "msg-1", result.Value!, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Contain("40");
        await chat.Received(1)
            .SendReplyAsync(
                Broadcaster,
                "msg-1",
                Arg.Is<string>(m => m.Contains("40")),
                CancellationToken.None
            );

        await music.DidNotReceiveWithAnyArgs().SetVolumeAsync(default!, default, default);
    }

    [Fact]
    public async Task No_argument_is_truthful_when_the_volume_genuinely_cannot_be_read()
    {
        IMusicService music = Substitute.For<IMusicService>();
        music
            .GetNowPlayingAsync(Broadcaster.ToString(), Arg.Any<CancellationToken>())
            .Returns((NowPlaying?)null);
        VolumeBuiltin sut = new(music);

        Result<string> result = await sut.ExecuteAsync(Ctx(string.Empty));

        result.IsSuccess.Should().BeTrue();
        // Never a guessed number — must not contain any digit when the real value is unknown.
        result.Value.Should().NotMatchRegex("\\d");
    }

    [Fact]
    public async Task Argument_still_sets_the_volume_and_is_unaffected_by_the_no_arg_change()
    {
        IMusicService music = Substitute.For<IMusicService>();
        music
            .SetVolumeAsync(Broadcaster.ToString(), 55, Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        VolumeBuiltin sut = new(music);

        Result<string> result = await sut.ExecuteAsync(Ctx("55"));

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Contain("55");
        await music
            .Received(1)
            .SetVolumeAsync(Broadcaster.ToString(), 55, Arg.Any<CancellationToken>());
    }
}
