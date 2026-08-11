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
using NomNomzBot.Domain.Music.Events;
using NomNomzBot.Domain.Music.Interfaces;
using NomNomzBot.Infrastructure.AutomationApi.Events;
using NomNomzBot.Infrastructure.Music;
using NomNomzBot.Infrastructure.Tests.Identity;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Music;

/// <summary>
/// Proves music-automation-controls.md D4's "never drift" claim end to end: <see cref="SongChangedProjector"/>
/// republishes the internal <see cref="PlaybackStateChangedEvent"/> (already fully enriched at the source —
/// MusicService/MusicStatePollingService, not re-read here) as the public <see cref="SongChangedEvent"/> using
/// the SAME <see cref="MusicAutomationProjection"/> helper the REST now-playing read uses, and
/// <see cref="SongChangedAutomationEventDescriptor"/>'s projection is a pure field passthrough — so the emitted
/// <c>song.changed</c> payload matches what <c>GetNowPlayingAsync</c> would render for the same state, with no
/// second live provider read (and therefore no race with a track boundary) in between.
/// </summary>
public sealed class SongChangedProjectorTests
{
    private static readonly Guid ChannelId = Guid.Parse("0192a000-0000-7000-8000-0000000f9001");
    private static readonly DateTimeOffset Now = new(2026, 8, 11, 12, 0, 0, TimeSpan.Zero);

    private static PlaybackStateChangedEvent Event() =>
        new()
        {
            BroadcasterId = ChannelId,
            IsPlaying = true,
            TrackName = "Track",
            Artist = "Artist",
            Album = "Album",
            AlbumArtUrl = "https://i.scdn.co/art.jpg",
            DurationMs = 5000,
            ProgressMs = 2500,
            Provider = "spotify",
            TrackUri = "spotify:track:x",
            ArtistId = "spotify:artist:9",
            ShuffleEnabled = true,
            RepeatMode = MusicRepeatMode.Context,
            VolumePercent = 62,
            ObservedAt = Now,
        };

    [Fact]
    public async Task Republishes_the_full_event_state_as_the_public_song_changed_event()
    {
        IMusicProviderManageApi musicManage = Substitute.For<IMusicProviderManageApi>();
        RecordingEventBus bus = new();
        musicManage
            .AreTracksSavedAsync(
                ChannelId,
                "spotify",
                Arg.Is<IReadOnlyList<string>>(l => l.Single() == "spotify:track:x"),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success<IReadOnlyList<bool>>([true]));

        SongChangedProjector sut = new(musicManage, bus, new FakeTimeProvider(Now));

        await sut.HandleAsync(Event());

        SongChangedEvent published = bus.Published.OfType<SongChangedEvent>().Single();
        published.BroadcasterId.Should().Be(ChannelId);
        published.Title.Should().Be("Track");
        published.PositionMs.Should().Be(2500);
        published.ShuffleEnabled.Should().BeTrue();
        published.RepeatMode.Should().Be("context");
        published.IsSaved.Should().BeTrue();
        published.VolumePercent.Should().Be(62);
        published.AlbumArtUrl.Should().Be("https://i.scdn.co/art.jpg");

        // The descriptor's projection is a pure passthrough of the already-projected event.
        SongChangedAutomationEventDescriptor descriptor = new();
        AutomationNowPlayingDto viaDescriptor = (AutomationNowPlayingDto)
            descriptor.ProjectPayload(published);
        viaDescriptor.VolumePercent.Should().Be(62);
        viaDescriptor.Title.Should().Be("Track");
        viaDescriptor.AlbumArtUrl.Should().Be("https://i.scdn.co/art.jpg");
    }

    [Fact]
    public async Task Skips_publishing_for_the_platform_sentinel_broadcaster()
    {
        IMusicProviderManageApi musicManage = Substitute.For<IMusicProviderManageApi>();
        RecordingEventBus bus = new();
        SongChangedProjector sut = new(musicManage, bus, new FakeTimeProvider(Now));

        await sut.HandleAsync(
            new PlaybackStateChangedEvent { BroadcasterId = Guid.Empty, IsPlaying = false }
        );

        bus.Published.Should().BeEmpty();
    }

    [Fact]
    public async Task Skips_publishing_when_nothing_is_playing()
    {
        IMusicProviderManageApi musicManage = Substitute.For<IMusicProviderManageApi>();
        RecordingEventBus bus = new();
        SongChangedProjector sut = new(musicManage, bus, new FakeTimeProvider(Now));

        // No TrackName / no Provider — the same "nothing playing" state GetNowPlayingAsync used to
        // signal by returning null, now read straight off the event.
        await sut.HandleAsync(
            new PlaybackStateChangedEvent { BroadcasterId = ChannelId, IsPlaying = false }
        );

        bus.Published.Should().BeEmpty();
    }

    [Fact]
    public async Task Never_races_a_second_provider_read_a_track_boundary_change_still_publishes()
    {
        // The bug this class exists to prevent: an earlier version re-read GetNowPlayingAsync a second
        // time and silently dropped the event if that read raced the provider and came back null — even
        // though the event that triggered this handler already carried a complete, valid snapshot.
        IMusicProviderManageApi musicManage = Substitute.For<IMusicProviderManageApi>();
        musicManage
            .AreTracksSavedAsync(
                ChannelId,
                "spotify",
                Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Failure<IReadOnlyList<bool>>("boom", "SERVICE_UNAVAILABLE"));
        RecordingEventBus bus = new();
        SongChangedProjector sut = new(musicManage, bus, new FakeTimeProvider(Now));

        await sut.HandleAsync(Event());

        // Even a failed saved-check (a real, still-possible I/O failure) degrades to null rather than
        // dropping the whole publish — the track/position/volume data is never gated on it.
        SongChangedEvent published = bus.Published.OfType<SongChangedEvent>().Single();
        published.IsSaved.Should().BeNull();
        published.Title.Should().Be("Track");
    }
}
