// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot. You may use, change and share it for free,
//  for any noncommercial purpose, under the PolyForm Noncommercial License
//  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
//  See LICENSE for details, and TRADEMARKS.md for the name and logo.
//
//  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// -----------------------------------------------------------------------------

using FluentAssertions;
using NomNomzBot.Domain.Music.ValueObjects;
using static NomNomzBot.Infrastructure.Tests.Music.PlaybackDirectorTestKit;

namespace NomNomzBot.Infrastructure.Tests.Music;

/// <summary>Row 28: Spotify moved to another device while our item plays.</summary>
public sealed partial class PlaybackDirectorTests
{
    private const string DeviceReason = "Spotify moved to another device";

    private static Task ReportOnDevice(Rig rig, PlaybackItem item, string? deviceId) =>
        rig.Spotify.EmitAsync(
            new EngineReport(EngineKind.Spotify, item.Uri, true, 5_000, item.DurationMs, deviceId)
        );

    [Fact]
    public async Task Row28_SpotifyReportFromAnotherDeviceDuringOurItem_BlocksWithNoEngineCall()
    {
        Rig rig = new();
        PlaybackItem item = Item("a", owner: "owner-a");
        await rig.Request(item);
        await ReportOnDevice(rig, item, "desk-pc");
        int callsBefore = rig.Spotify.Calls.Count;

        await ReportOnDevice(rig, item, "phone");

        rig.Director.State.Should().Be(PlaybackState.Blocked(DeviceReason));
        rig.Spotify.Calls.Should().HaveCount(callsBefore);
        rig.YouTube.Calls.Should().BeEmpty();
        rig.Journal.Last().Reason.Should().Be("device_changed");
    }

    [Fact]
    public async Task Row28_ReportsFromTheSameDeviceOrWithoutADeviceId_KeepPlaying()
    {
        Rig rig = new();
        PlaybackItem item = Item("a", owner: "owner-a");
        await rig.Request(item);
        await ReportOnDevice(rig, item, "desk-pc");

        await ReportOnDevice(rig, item, "desk-pc");
        await ReportOnDevice(rig, item, null);

        rig.Director.State.Kind.Should().Be(PlaybackStateKind.Playing);
    }
}
