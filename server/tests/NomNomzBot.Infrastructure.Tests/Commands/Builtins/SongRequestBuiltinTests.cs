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
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Infrastructure.Commands.Builtins;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Commands.Builtins;

/// <summary>
/// Proves <see cref="SongRequestBuiltin"/> answers each refusal reason with its own honest wording instead
/// of one blanket "could not add" — a genuinely-not-found query, a blocked track, and "no music provider
/// connected" are different problems and must read differently in chat. The no-provider case additionally
/// depends on WHO asked: only the broadcaster can authorize a Spotify/YouTube connection (dashboard OAuth),
/// so a viewer or mod is never told to do something only the broadcaster can do.
/// </summary>
public sealed class SongRequestBuiltinTests
{
    private static readonly Guid Broadcaster = Guid.Parse("0192a000-0000-7000-8000-000000009901");

    [Fact]
    public async Task No_provider_connected_tells_the_broadcaster_to_connect_it_themselves()
    {
        SongRequestBuiltin sut = Build(
            requestResult: Result.Failure<MusicTrack>(
                "No active music provider.",
                "SERVICE_UNAVAILABLE"
            )
        );

        Result<string> result = await sut.ExecuteAsync(Context("lofi beats", roleLevel: 40));

        result.Value.Should().Contain("connect Spotify or YouTube in the dashboard");
    }

    [Fact]
    public async Task No_provider_connected_tells_a_mod_to_flag_it_to_the_broadcaster_not_to_connect_it()
    {
        SongRequestBuiltin sut = Build(
            requestResult: Result.Failure<MusicTrack>(
                "No active music provider.",
                "SERVICE_UNAVAILABLE"
            )
        );

        Result<string> result = await sut.ExecuteAsync(Context("lofi beats", roleLevel: 10));

        result
            .Value.Should()
            .Be(
                "Song requests aren't connected — let the broadcaster know to connect Spotify or YouTube in the dashboard."
            );
    }

    [Fact]
    public async Task No_provider_connected_gives_a_viewer_no_detail_the_command_just_reads_as_disabled()
    {
        SongRequestBuiltin sut = Build(
            requestResult: Result.Failure<MusicTrack>(
                "No active music provider.",
                "SERVICE_UNAVAILABLE"
            )
        );

        Result<string> result = await sut.ExecuteAsync(Context("lofi beats", roleLevel: 0));

        result.Value.Should().Be("This command is currently disabled.");
        result.Value.Should().NotContain("Spotify");
        result.Value.Should().NotContain("YouTube");
        result.Value.Should().NotContain("broadcaster");
    }

    [Fact]
    public async Task A_blocked_track_carries_its_typed_reason_regardless_of_role()
    {
        SongRequestBuiltin sut = Build(
            requestResult: Result.Failure<MusicTrack>(
                "\"Song Q\" is blocked in this channel.",
                "TRACK_BLOCKED"
            )
        );

        Result<string> result = await sut.ExecuteAsync(Context("song q", roleLevel: 0));

        result.Value.Should().Be("\"Song Q\" is blocked in this channel.");
    }

    [Fact]
    public async Task A_provider_error_reads_differently_from_no_provider_and_from_not_found()
    {
        SongRequestBuiltin sut = Build(
            requestResult: Result.Failure<MusicTrack>("token refresh failed", "PROVIDER_ERROR")
        );

        Result<string> result = await sut.ExecuteAsync(Context("lofi beats", roleLevel: 0));

        result.Value.Should().Contain("try again in a moment");
        result.Value.Should().NotContain("aren't available");
        result.Value.Should().NotContain("No tracks found");
    }

    // ─── Harness ──────────────────────────────────────────────────────────────

    private static SongRequestBuiltin Build(Result<MusicTrack> requestResult)
    {
        IMusicService music = Substitute.For<IMusicService>();
        music
            .RequestTrackAsync(
                Broadcaster.ToString(),
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(requestResult);

        IBuiltinResponseComposer composer = Substitute.For<IBuiltinResponseComposer>();
        composer
            .ComposeAsync(Arg.Any<BuiltinResponseRequest>(), Arg.Any<CancellationToken>())
            .Returns(ci => Task.FromResult(ci.Arg<BuiltinResponseRequest>().NeutralFallback));

        return new SongRequestBuiltin(music, composer);
    }

    private static BuiltinCommandContext Context(string args, int roleLevel) =>
        new()
        {
            BroadcasterId = Broadcaster,
            TriggeringUserId = "viewer-1",
            TriggeringUserDisplayName = "Viewer",
            RoleLevel = roleLevel,
            Args = args,
        };
}
