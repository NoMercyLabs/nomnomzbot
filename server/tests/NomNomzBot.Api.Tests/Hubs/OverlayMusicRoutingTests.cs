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
using NomNomzBot.Api.Hubs.Overlay;

namespace NomNomzBot.Api.Tests.Hubs;

/// <summary>
/// The Music Player widget is its own output: YouTube and Spotify playback routes to the newest
/// <c>music_player</c> page, and an Audio page never counts as one (and the other way round).
/// </summary>
public sealed class OverlayMusicRoutingTests
{
    private static readonly Guid Broadcaster = Guid.Parse("0192b000-0000-7000-8000-0000000b7d01");
    private static readonly Guid Other = Guid.Parse("0192b000-0000-7000-8000-0000000b7d02");
    private static readonly Guid MusicA = Guid.Parse("0192b000-0000-7000-8000-0000000b7d0a");
    private static readonly Guid MusicB = Guid.Parse("0192b000-0000-7000-8000-0000000b7d0b");
    private static readonly Guid AudioWidget = Guid.Parse("0192b000-0000-7000-8000-0000000b7d0c");

    [Fact]
    public void Music_target_is_the_newest_music_source()
    {
        OverlayPresenceRegistry registry = new();
        registry.RegisterOverlay("c1", Broadcaster);
        registry.RegisterOverlay("c2", Broadcaster);
        registry.MarkMusicSource("c1", MusicA);
        registry.MarkMusicSource("c2", MusicB);

        registry.GetMusicTarget(Broadcaster).Should().Be("c2");
        registry.GetMusicWidgetId(Broadcaster).Should().Be(MusicB);
        registry.IsMusicSourceConnected(Broadcaster).Should().BeTrue();
    }

    [Fact]
    public void An_audio_source_is_not_a_music_source_and_the_other_way_round()
    {
        OverlayPresenceRegistry registry = new();
        registry.RegisterOverlay("a", Broadcaster);
        registry.RegisterOverlay("m", Broadcaster);
        registry.MarkAudioSource("a", AudioWidget);

        registry.IsMusicSourceConnected(Broadcaster).Should().BeFalse();
        registry.GetMusicTarget(Broadcaster).Should().BeNull();
        registry.GetMusicWidgetId(Broadcaster).Should().BeNull();

        registry.MarkMusicSource("m", MusicA);

        registry.GetAudioTarget(Broadcaster).Should().Be("a");
        registry.GetAudioWidgetId(Broadcaster).Should().Be(AudioWidget);
        registry.GetMusicTarget(Broadcaster).Should().Be("m");
    }

    [Fact]
    public void Music_sources_of_another_channel_are_ignored()
    {
        OverlayPresenceRegistry registry = new();
        registry.RegisterOverlay("x", Other);
        registry.MarkMusicSource("x", MusicA);

        registry.IsMusicSourceConnected(Broadcaster).Should().BeFalse();
        registry.GetMusicTarget(Broadcaster).Should().BeNull();
        registry.GetMusicTarget(Other).Should().Be("x");
    }

    [Fact]
    public void Drop_clears_the_music_source()
    {
        OverlayPresenceRegistry registry = new();
        registry.RegisterOverlay("c1", Broadcaster);
        registry.MarkMusicSource("c1", MusicA);

        registry.Drop("c1");

        registry.IsMusicSourceConnected(Broadcaster).Should().BeFalse();
        registry.GetMusicTarget(Broadcaster).Should().BeNull();
        registry.GetMusicWidgetId(Broadcaster).Should().BeNull();
    }

    [Fact]
    public void A_second_music_source_displaces_the_first_and_reports_it()
    {
        OverlayPresenceRegistry registry = new();
        registry.RegisterOverlay("c1", Broadcaster);
        registry.RegisterOverlay("c2", Broadcaster);

        (string ConnectionId, Guid? WidgetId)? first = registry.MarkMusicSource("c1", MusicA);
        (string ConnectionId, Guid? WidgetId)? second = registry.MarkMusicSource("c2", MusicB);

        first.Should().BeNull();
        second.Should().NotBeNull();
        second!.Value.ConnectionId.Should().Be("c1");
        second.Value.WidgetId.Should().Be(MusicA);
    }

    [Fact]
    public void Re_marking_the_same_connection_displaces_nobody()
    {
        OverlayPresenceRegistry registry = new();
        registry.RegisterOverlay("c1", Broadcaster);
        registry.MarkMusicSource("c1", MusicA);

        registry.MarkMusicSource("c1", MusicA).Should().BeNull();
    }
}
