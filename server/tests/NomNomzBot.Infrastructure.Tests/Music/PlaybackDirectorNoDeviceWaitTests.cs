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
using NomNomzBot.Domain.Music.ValueObjects;
using static NomNomzBot.Infrastructure.Tests.Music.PlaybackDirectorTestKit;

namespace NomNomzBot.Infrastructure.Tests.Music;

/// <summary>
/// Live 2026-10-20: with no Spotify device the director started the blocked head again on every 5 s poll tick
/// and journaled each try. A head blocked for "no output" waits, and only a signal that changes the answer
/// starts it again.
/// </summary>
public sealed partial class PlaybackDirectorTests
{
    private static async Task<(Rig Rig, PlaybackItem Head)> RigWithNoSpotifyDeviceAsync()
    {
        Rig rig = new();
        rig.Spotify.BlockCommands = EngineBlockReason.NoOutput;
        PlaybackItem head = Item("a");
        await rig.Request(head);

        return (rig, head);
    }

    private static async Task PollTicksAsync(Rig rig, int seconds)
    {
        for (int elapsed = 0; elapsed < seconds; elapsed += 5)
        {
            rig.Clock.Advance(TimeSpan.FromSeconds(5));
            await rig.Director.HandleAsync(new RequestAdded());
        }
    }

    private static int StartsOf(Rig rig) => rig.Spotify.Calls.Count(c => c.Name == "Start");

    [Fact]
    public async Task NoDevice_SixtySecondsOfPollTicksCauseOneStartAndOneBlockEntry()
    {
        (Rig rig, PlaybackItem head) = await RigWithNoSpotifyDeviceAsync();
        int journalAfterBlock = rig.Journal.Count;

        await PollTicksAsync(rig, 60);

        StartsOf(rig).Should().Be(1);
        rig.Journal.Should().HaveCount(journalAfterBlock);
        rig.Journal.Select(j => j.Reason).Should().Equal("start_head", "start_refused");
        rig.Director.State.Should().Be(PlaybackState.Blocked("NoOutput"));
        rig.QueueIds.Should().Equal(head.Id);
    }

    [Fact]
    public async Task NoDevice_ADeviceAppearingStartsTheHeadOnceAndTheClearIsJournaled()
    {
        (Rig rig, PlaybackItem head) = await RigWithNoSpotifyDeviceAsync();
        await PollTicksAsync(rig, 30);
        rig.Spotify.BlockCommands = null;

        for (int poll = 0; poll < 3; poll++)
            await rig.Spotify.EmitAsync(
                new EngineReport(EngineKind.Spotify, "spotify:track:old", false, 0, 1_000, "dev-1")
            );

        StartsOf(rig).Should().Be(2);
        rig.Spotify.OutputBlockForgotten.Should().Be(1);
        rig.Director.State.Should().Be(PlaybackState.Starting(EngineKind.Spotify, head));
        rig.Journal.Select(j => j.Reason)
            .Should()
            .Equal("start_head", "start_refused", "output_available", "start_head");
    }

    [Fact]
    public async Task NoDevice_TheDeviceThatAlreadyFailedDoesNotStartTheHeadAgain()
    {
        (Rig rig, _) = await RigWithNoSpotifyDeviceAsync();
        await rig.Spotify.EmitAsync(
            new EngineReport(EngineKind.Spotify, "spotify:track:old", false, 0, 1_000, "dev-1")
        );
        int startsAfterDevice = StartsOf(rig);

        for (int poll = 0; poll < 4; poll++)
            await rig.Spotify.EmitAsync(
                new EngineReport(EngineKind.Spotify, "spotify:track:old", false, 0, 1_000, "dev-1")
            );

        StartsOf(rig).Should().Be(startsAfterDevice);
    }

    [Fact]
    public async Task NoDevice_ANewRequestChangesTheQueueAndTriesTheHeadOnce()
    {
        (Rig rig, _) = await RigWithNoSpotifyDeviceAsync();
        await PollTicksAsync(rig, 30);

        await rig.Request(Item("b"));
        await PollTicksAsync(rig, 30);

        StartsOf(rig).Should().Be(2);
        rig.Spotify.OutputBlockForgotten.Should().Be(0);
    }

    [Fact]
    public async Task NoDevice_ThePlayerPageAttachingTriesTheHeadOnce()
    {
        (Rig rig, _) = await RigWithNoSpotifyDeviceAsync();
        await PollTicksAsync(rig, 30);

        await rig.Director.HandleAsync(new OutputAttached());
        await PollTicksAsync(rig, 30);

        StartsOf(rig).Should().Be(2);
    }

    [Fact]
    public async Task NoDevice_TheStreamerPressingPlayTriesTheHeadOnce()
    {
        (Rig rig, _) = await RigWithNoSpotifyDeviceAsync();
        await PollTicksAsync(rig, 30);

        await rig.Director.HandleAsync(new OwnerResume());
        await PollTicksAsync(rig, 30);

        StartsOf(rig).Should().Be(2);
    }
}
