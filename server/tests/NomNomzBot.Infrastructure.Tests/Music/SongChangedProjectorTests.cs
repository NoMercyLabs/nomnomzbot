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
using NomNomzBot.Application.AutomationApi.Dtos;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Music;
using NomNomzBot.Application.Music.Services;
using NomNomzBot.Domain.Music.Events;
using NomNomzBot.Domain.Music.Interfaces;
using NomNomzBot.Infrastructure.AutomationApi.Events;
using NomNomzBot.Infrastructure.Music;
using NomNomzBot.Infrastructure.Tests.Identity;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Music;

/// <summary>
/// Proves music-automation-controls.md D4's "never drift" claim end to end: <see cref="SongChangedProjector"/>
/// republishes the internal <see cref="PlaybackStateChangedEvent"/> as the public
/// <see cref="SongChangedEvent"/> using the SAME <see cref="MusicAutomationProjection"/> helper the
/// REST now-playing read uses, and <see cref="SongChangedAutomationEventDescriptor"/>'s projection is a
/// pure field passthrough — so the emitted <c>song.changed</c> payload is byte-identical to what
/// <c>GetNowPlayingAsync</c> would render for the same state.
/// </summary>
public sealed class SongChangedProjectorTests
{
    private static readonly Guid ChannelId = Guid.Parse("0192a000-0000-7000-8000-0000000f9001");
    private static readonly DateTimeOffset Now = new(2026, 8, 11, 12, 0, 0, TimeSpan.Zero);

    private static NowPlaying Track() =>
        new(
            "Track",
            "Artist",
            "Album",
            null,
            5000,
            2500,
            true,
            80,
            null,
            "spotify",
            "spotify:track:x",
            true,
            MusicRepeatMode.Context,
            "spotify:artist:9"
        );

    [Fact]
    public async Task Republishes_the_full_live_state_as_the_public_song_changed_event()
    {
        IMusicService music = Substitute.For<IMusicService>();
        IMusicProviderManageApi musicManage = Substitute.For<IMusicProviderManageApi>();
        RecordingEventBus bus = new();
        NowPlaying track = Track();

        music.GetNowPlayingAsync(ChannelId.ToString(), Arg.Any<CancellationToken>()).Returns(track);
        music
            .GetActiveProviderKeyAsync(ChannelId.ToString(), Arg.Any<CancellationToken>())
            .Returns("spotify");
        musicManage
            .AreTracksSavedAsync(
                ChannelId,
                "spotify",
                Arg.Is<IReadOnlyList<string>>(l => l.Single() == "spotify:track:x"),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success<IReadOnlyList<bool>>([true]));

        SongChangedProjector sut = new(music, musicManage, bus, new FakeTimeProvider(Now));

        await sut.HandleAsync(
            new PlaybackStateChangedEvent { BroadcasterId = ChannelId, IsPlaying = true }
        );

        SongChangedEvent published = bus.Published.OfType<SongChangedEvent>().Single();
        published.BroadcasterId.Should().Be(ChannelId);
        published.Title.Should().Be("Track");
        published.PositionMs.Should().Be(2500);
        published.ShuffleEnabled.Should().BeTrue();
        published.RepeatMode.Should().Be("context");
        published.IsSaved.Should().BeTrue();

        // The descriptor's projection is a pure passthrough of the already-projected event — proves
        // the automation stream payload can never diverge from the shared helper's output.
        SongChangedAutomationEventDescriptor descriptor = new();
        AutomationNowPlayingDto viaDescriptor = (AutomationNowPlayingDto)
            descriptor.ProjectPayload(published);
        AutomationNowPlayingDto viaSharedHelper = await MusicAutomationProjection.ToNowPlayingAsync(
            track,
            "spotify",
            ChannelId,
            musicManage,
            new FakeTimeProvider(Now),
            CancellationToken.None
        );
        viaDescriptor.Should().Be(viaSharedHelper);
    }

    [Fact]
    public async Task Skips_publishing_for_the_platform_sentinel_broadcaster()
    {
        IMusicService music = Substitute.For<IMusicService>();
        IMusicProviderManageApi musicManage = Substitute.For<IMusicProviderManageApi>();
        RecordingEventBus bus = new();
        SongChangedProjector sut = new(music, musicManage, bus, new FakeTimeProvider(Now));

        await sut.HandleAsync(
            new PlaybackStateChangedEvent { BroadcasterId = Guid.Empty, IsPlaying = false }
        );

        bus.Published.Should().BeEmpty();
        await music
            .DidNotReceive()
            .GetNowPlayingAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Skips_publishing_when_no_provider_is_connected()
    {
        IMusicService music = Substitute.For<IMusicService>();
        IMusicProviderManageApi musicManage = Substitute.For<IMusicProviderManageApi>();
        RecordingEventBus bus = new();
        music
            .GetActiveProviderKeyAsync(ChannelId.ToString(), Arg.Any<CancellationToken>())
            .Returns((string?)null);
        SongChangedProjector sut = new(music, musicManage, bus, new FakeTimeProvider(Now));

        await sut.HandleAsync(
            new PlaybackStateChangedEvent { BroadcasterId = ChannelId, IsPlaying = true }
        );

        bus.Published.Should().BeEmpty();
    }
}
