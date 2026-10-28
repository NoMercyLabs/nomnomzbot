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
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Widgets.Services;
using NomNomzBot.Domain.Widgets.Entities;
using NomNomzBot.Infrastructure.Music;
using NomNomzBot.Infrastructure.Tests.Identity;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Music;

/// <summary>
/// YouTube plays on the Music Player widget (gallery key <c>music_player</c>), never on the Audio page.
/// </summary>
public sealed class YouTubeMusicWidgetRoutingTests
{
    private static readonly Guid Broadcaster = Guid.Parse("0192b000-0000-7000-8000-0000000c7d01");
    private static readonly Guid MusicWidget = Guid.Parse("0192b000-0000-7000-8000-0000000c7d02");
    private static readonly Guid AudioWidget = Guid.Parse("0192b000-0000-7000-8000-0000000c7d03");

    private sealed class RecordingNotifier : IWidgetEventNotifier
    {
        public List<(Guid Widget, string EventType)> Sent { get; } = [];

        public Task SendWidgetEventAsync(
            Guid broadcasterId,
            Guid widgetId,
            string eventType,
            object? data,
            CancellationToken ct = default
        )
        {
            Sent.Add((widgetId, eventType));
            return Task.CompletedTask;
        }
    }

    private sealed record Rig(
        YouTubePlayerDispatcher Players,
        RecordingNotifier Notifier,
        IOverlayPresenceRegistry Presence
    );

    private static async Task<Rig> BuildAsync()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        db.Widgets.Add(
            new Widget
            {
                Id = MusicWidget,
                BroadcasterId = Broadcaster,
                Name = "Music Player",
                IsEnabled = true,
            }
        );
        db.Widgets.Add(
            new Widget
            {
                Id = AudioWidget,
                BroadcasterId = Broadcaster,
                Name = "Audio",
                IsEnabled = true,
            }
        );
        await db.SaveChangesAsync();

        IOverlayPresenceRegistry presence = Substitute.For<IOverlayPresenceRegistry>();
        RecordingNotifier notifier = new();
        YouTubePlayerDispatcher players = new(
            db,
            presence,
            notifier,
            new YouTubePlayerStateStore(new FakeTimeProvider())
        );
        return new(players, notifier, presence);
    }

    [Fact]
    public async Task An_audio_page_and_a_music_widget_attached_play_on_the_music_widget_only()
    {
        Rig rig = await BuildAsync();
        rig.Presence.GetAudioWidgetId(Broadcaster).Returns(AudioWidget);
        rig.Presence.GetMusicWidgetId(Broadcaster).Returns(MusicWidget);

        IReadOnlyList<Guid> found = await rig.Players.FindPlayersAsync(Broadcaster);
        await rig.Players.PlayAsync(Broadcaster, found, "dQw4w9WgXcQ");

        found.Should().Equal(MusicWidget);
        rig.Notifier.Sent.Should().Equal((MusicWidget, YouTubePlayerDispatcher.PlayEventType));
    }

    [Fact]
    public async Task Only_an_audio_page_attached_finds_no_player()
    {
        Rig rig = await BuildAsync();
        rig.Presence.GetAudioWidgetId(Broadcaster).Returns(AudioWidget);
        rig.Presence.GetMusicWidgetId(Broadcaster).Returns((Guid?)null);

        IReadOnlyList<Guid> found = await rig.Players.FindPlayersAsync(Broadcaster);

        found.Should().BeEmpty();
        rig.Notifier.Sent.Should().BeEmpty();
    }

    [Fact]
    public async Task A_music_widget_attaching_makes_the_player_findable()
    {
        Rig rig = await BuildAsync();
        rig.Presence.GetMusicWidgetId(Broadcaster).Returns((Guid?)null);
        (await rig.Players.FindPlayersAsync(Broadcaster)).Should().BeEmpty();

        rig.Presence.GetMusicWidgetId(Broadcaster).Returns(MusicWidget);

        (await rig.Players.FindPlayersAsync(Broadcaster)).Should().Equal(MusicWidget);
    }
}
