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
using Microsoft.AspNetCore.SignalR;
using NomNomzBot.Api.Hubs;
using NomNomzBot.Api.Hubs.Clients;
using NomNomzBot.Api.Hubs.Dtos;
using NomNomzBot.Api.Hubs.Overlay;
using NSubstitute;

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
        second.Value.ConnectionId.Should().Be("c1");
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

    [Theory]
    [InlineData("youtube.play")]
    [InlineData("youtube.pause")]
    [InlineData("youtube.resume")]
    [InlineData("youtube.stop")]
    [InlineData("youtube.seek")]
    [InlineData("youtube.volume")]
    public async Task A_youtube_command_reaches_the_music_player_page_and_never_the_audio_page(
        string eventType
    )
    {
        // qtkitte live, 2026-10-19: every YouTube command went to the Bot Audio page, so the Music Player
        // never got youtube.play and no YouTube request ever played.
        OverlayPresenceRegistry registry = new();
        registry.RegisterOverlay("audio-conn", Broadcaster);
        registry.MarkAudioSource("audio-conn", AudioWidget);
        registry.RegisterOverlay("music-conn", Broadcaster);
        registry.MarkMusicSource("music-conn", MusicA);
        IOverlayClient audio = Substitute.For<IOverlayClient>();
        IOverlayClient music = Substitute.For<IOverlayClient>();
        IHubClients<IOverlayClient> clients = Substitute.For<IHubClients<IOverlayClient>>();
        clients.Client("audio-conn").Returns(audio);
        clients.Client("music-conn").Returns(music);
        IHubContext<OverlayHub, IOverlayClient> hub = Substitute.For<
            IHubContext<OverlayHub, IOverlayClient>
        >();
        hub.Clients.Returns(clients);
        WidgetEventNotifierAdapter notifier = new(new WidgetNotifier(hub, registry));

        await notifier.SendWidgetEventAsync(
            Broadcaster,
            MusicA,
            eventType,
            new { videoId = "M7lc1UVf-VE" }
        );

        await music
            .Received(1)
            .WidgetEvent(
                Arg.Is<WidgetEventDto>(e =>
                    e.EventType == eventType && e.WidgetId == MusicA.ToString()
                )
            );
        await audio.DidNotReceiveWithAnyArgs().WidgetEvent(default!);
    }

    [Fact]
    public async Task A_youtube_command_with_no_music_player_page_open_reaches_no_page()
    {
        OverlayPresenceRegistry registry = new();
        registry.RegisterOverlay("audio-conn", Broadcaster);
        registry.MarkAudioSource("audio-conn", AudioWidget);
        IOverlayClient audio = Substitute.For<IOverlayClient>();
        IHubClients<IOverlayClient> clients = Substitute.For<IHubClients<IOverlayClient>>();
        clients.Client("audio-conn").Returns(audio);
        IHubContext<OverlayHub, IOverlayClient> hub = Substitute.For<
            IHubContext<OverlayHub, IOverlayClient>
        >();
        hub.Clients.Returns(clients);
        WidgetEventNotifierAdapter notifier = new(new WidgetNotifier(hub, registry));

        await notifier.SendWidgetEventAsync(Broadcaster, MusicA, "youtube.play", null);

        await audio.DidNotReceiveWithAnyArgs().WidgetEvent(default!);
        clients.DidNotReceiveWithAnyArgs().Group(default!);
    }
}
