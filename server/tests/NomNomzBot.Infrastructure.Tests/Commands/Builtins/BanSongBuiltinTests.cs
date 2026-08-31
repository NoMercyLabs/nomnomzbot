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
using NomNomzBot.Application.Music.Dtos;
using NomNomzBot.Application.Music.Services;
using NomNomzBot.Infrastructure.Commands.Builtins;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Commands.Builtins;

/// <summary>
/// <c>!bansong</c> (legacy parity, S068c) proves the REAL side effect: the currently playing track is
/// actually handed to <see cref="IBlockedTrackService"/> with the right provider/URI/title, not merely
/// "no exception" — and that nothing is banned when nothing is playing.
/// </summary>
public sealed class BanSongBuiltinTests
{
    private static readonly Guid Broadcaster = Guid.Parse("0192a000-0000-7000-8000-000000009902");

    private static BuiltinCommandContext Context() =>
        new()
        {
            BroadcasterId = Broadcaster,
            TriggeringUserId = "mod-1",
            TriggeringUserDisplayName = "SomeMod",
        };

    [Fact]
    public async Task Banning_the_playing_track_calls_BlockAsync_with_its_real_provider_uri_and_title()
    {
        IMusicService music = Substitute.For<IMusicService>();
        music
            .GetNowPlayingAsync(Broadcaster.ToString(), Arg.Any<CancellationToken>())
            .Returns(
                new NowPlaying(
                    TrackName: "Never Gonna Give You Up",
                    Artist: "Rick Astley",
                    Album: null,
                    ImageUrl: null,
                    DurationMs: 213_000,
                    ProgressMs: 1_000,
                    IsPlaying: true,
                    Volume: 50,
                    RequestedBy: "viewer1",
                    Provider: "spotify",
                    TrackUri: "spotify:track:rick1"
                )
            );

        IBlockedTrackService blockedTracks = Substitute.For<IBlockedTrackService>();
        blockedTracks
            .BlockAsync(Broadcaster, Arg.Any<BlockTrackRequest>(), Arg.Any<CancellationToken>())
            .Returns(
                Result.Success(
                    new BlockedTrackDto(
                        Guid.CreateVersion7(),
                        "spotify",
                        "spotify:track:rick1",
                        "Never Gonna Give You Up",
                        "Banned via !bansong",
                        "mod-1",
                        DateTime.UtcNow
                    )
                )
            );

        BanSongBuiltin sut = new(music, blockedTracks);

        Result<string> result = await sut.ExecuteAsync(Context());

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Contain("Never Gonna Give You Up");

        await blockedTracks
            .Received(1)
            .BlockAsync(
                Broadcaster,
                Arg.Is<BlockTrackRequest>(r =>
                    r.Provider == "spotify"
                    && r.TrackUri == "spotify:track:rick1"
                    && r.Title == "Never Gonna Give You Up"
                    && r.BlockedByUserId == "mod-1"
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task Nothing_playing_never_calls_BlockAsync()
    {
        IMusicService music = Substitute.For<IMusicService>();
        music
            .GetNowPlayingAsync(Broadcaster.ToString(), Arg.Any<CancellationToken>())
            .Returns((NowPlaying?)null);

        IBlockedTrackService blockedTracks = Substitute.For<IBlockedTrackService>();

        BanSongBuiltin sut = new(music, blockedTracks);

        Result<string> result = await sut.ExecuteAsync(Context());

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Contain("Nothing is playing");

        await blockedTracks
            .DidNotReceive()
            .BlockAsync(
                Arg.Any<Guid>(),
                Arg.Any<BlockTrackRequest>(),
                Arg.Any<CancellationToken>()
            );
    }
}
