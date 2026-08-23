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
using NomNomzBot.Domain.Music.Events;
using NomNomzBot.Infrastructure.Music;
using NomNomzBot.Infrastructure.Tests.Identity;

namespace NomNomzBot.Infrastructure.Tests.Music;

/// <summary>
/// The fair queue is drained by what is actually playing, never by an optimistic assumption at request or
/// skip time. Proves the three cases that matter live: the head starting, a Spotify client-side crossfade
/// (0–12s) or a multi-track skip carrying playback past entries that were never observed as current, and a
/// track that has nothing to do with the queue leaving it untouched.
/// </summary>
public sealed class SongRequestQueueReconcilerTests
{
    private static readonly Guid ChannelId = Guid.Parse("0192a000-0000-7000-8000-000000007901");

    [Fact]
    public async Task The_entry_for_the_track_that_started_playing_leaves_the_queue()
    {
        (SongRequestQueueReconciler sut, FairQueue<SongRequestEntry> queue, RecordingEventBus bus) =
            Build("a", "b", "c");

        await sut.HandleAsync(PlaybackOf("spotify:track:a"));

        Titles(queue).Should().Equal("Track b", "Track c");
        bus.Published.OfType<SongRequestQueueChangedEvent>()
            .Single()
            .Items.Select(i => i.Title)
            .Should()
            .Equal("Track b", "Track c");
    }

    [Fact]
    public async Task A_crossfade_or_multi_skip_that_lands_further_down_drops_everything_it_passed()
    {
        (SongRequestQueueReconciler sut, FairQueue<SongRequestEntry> queue, _) = Build(
            "a",
            "b",
            "c"
        );

        // Playback is observed on "c" without "a" or "b" ever being seen as current — a 12s crossfade
        // overlapping the poll cadence, or a viewer skipping twice between ticks.
        await sut.HandleAsync(PlaybackOf("spotify:track:c"));

        Titles(queue).Should().BeEmpty();
    }

    [Fact]
    public async Task A_track_that_is_not_in_the_queue_leaves_it_untouched()
    {
        (SongRequestQueueReconciler sut, FairQueue<SongRequestEntry> queue, RecordingEventBus bus) =
            Build("a", "b");

        // The streamer's own playlist track, not a request — our pending entries all still lie ahead.
        await sut.HandleAsync(PlaybackOf("spotify:track:something-else"));

        Titles(queue).Should().Equal("Track a", "Track b");
        bus.Published.OfType<SongRequestQueueChangedEvent>().Should().BeEmpty();
    }

    private static PlaybackStateChangedEvent PlaybackOf(string trackUri) =>
        new()
        {
            BroadcasterId = ChannelId,
            IsPlaying = true,
            TrackUri = trackUri,
            TrackName = trackUri,
            Provider = "spotify",
            ObservedAt = DateTimeOffset.UtcNow,
        };

    private static IEnumerable<string> Titles(FairQueue<SongRequestEntry> queue) =>
        queue.GetSnapshot().Select(e => e.Item.TrackName);

    private static (
        SongRequestQueueReconciler Sut,
        FairQueue<SongRequestEntry> Queue,
        RecordingEventBus Bus
    ) Build(params string[] trackIds)
    {
        SongRequestQueueStore store = new();
        FairQueue<SongRequestEntry> queue = store.GetOrCreate(ChannelId.ToString());
        foreach (string id in trackIds)
            queue.Enqueue(
                $"viewer-{id}",
                new($"spotify:track:{id}", $"Track {id}", "Artist", null, 200000, $"viewer-{id}")
            );

        RecordingEventBus bus = new();
        return (new SongRequestQueueReconciler(store, bus), queue, bus);
    }
}
