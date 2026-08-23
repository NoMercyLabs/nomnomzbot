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
    public async Task The_handed_over_track_starting_frees_the_provider_and_hands_over_the_next()
    {
        (
            SongRequestQueueReconciler sut,
            FairQueue<SongRequestEntry> queue,
            RecordingEventBus bus,
            SongRequestQueueStore store,
            RecordingHandover handover
        ) = Build("a", "b", "c");
        HandOver(store, queue, "a");

        await sut.HandleAsync(PlaybackOf("spotify:track:a"));

        Titles(queue).Should().Equal("Track b", "Track c");
        // Exactly one track is ever at the provider: "a" started, so the provider is free and the NEXT
        // request is handed over — not the whole queue.
        handover.Calls.Should().ContainSingle().Which.Should().Be(ChannelId.ToString());
        store.GetInFlight(ChannelId.ToString()).Should().BeNull("the handover records the new one");
        bus.Published.OfType<SongRequestQueueChangedEvent>()
            .Single()
            .Items.Select(i => i.Title)
            .Should()
            .Equal("Track b", "Track c");
    }

    [Fact]
    public async Task A_track_that_is_not_ours_while_one_is_handed_over_hands_over_nothing()
    {
        (
            SongRequestQueueReconciler sut,
            FairQueue<SongRequestEntry> queue,
            _,
            SongRequestQueueStore store,
            RecordingHandover handover
        ) = Build("a", "b");
        HandOver(store, queue, "a");

        // The streamer's own playlist track is playing; our handed-over request has NOT started yet, so
        // pushing another one now would put two of ours at the provider and freeze their order.
        await sut.HandleAsync(PlaybackOf("spotify:track:streamer-playlist"));

        handover.Calls.Should().BeEmpty();
        Titles(queue).Should().Equal("Track a", "Track b");
        store.GetInFlight(ChannelId.ToString())!.TrackName.Should().Be("Track a");
    }

    [Fact]
    public async Task A_crossfade_or_multi_skip_that_lands_further_down_drops_everything_it_passed()
    {
        (
            SongRequestQueueReconciler sut,
            FairQueue<SongRequestEntry> queue,
            _,
            SongRequestQueueStore store,
            RecordingHandover handover
        ) = Build("a", "b", "c");
        HandOver(store, queue, "a");

        // Playback is observed on "c" without "a" or "b" ever being seen as current — a 12s crossfade
        // overlapping the poll cadence, or a viewer skipping twice between ticks.
        await sut.HandleAsync(PlaybackOf("spotify:track:c"));

        Titles(queue).Should().BeEmpty();
        // The provider is free again — and with nothing left pending there is nothing to hand over, so
        // the reconciler must NOT push anything rather than re-pushing a track that already played.
        store.GetInFlight(ChannelId.ToString()).Should().BeNull();
        handover.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task A_track_that_is_not_in_the_queue_leaves_it_untouched()
    {
        (
            SongRequestQueueReconciler sut,
            FairQueue<SongRequestEntry> queue,
            RecordingEventBus bus,
            _,
            _
        ) = Build("a", "b");

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

    /// <summary>Marks one queued entry as the request currently sitting at the provider — the state the
    /// request path leaves behind when it hands a track over.</summary>
    private static void HandOver(
        SongRequestQueueStore store,
        FairQueue<SongRequestEntry> queue,
        string trackId
    ) =>
        store.SetInFlight(
            ChannelId.ToString(),
            queue.GetSnapshot().Single(e => e.Item.TrackUri == $"spotify:track:{trackId}").Item
        );

    /// <summary>Records handover requests instead of reaching a provider — the reconciler's decision is
    /// WHETHER to hand over; MusicService owns the push itself.</summary>
    private sealed class RecordingHandover : ISongRequestHandover
    {
        public List<string> Calls { get; } = [];

        public Task HandOverNextAsync(
            string broadcasterId,
            CancellationToken cancellationToken = default
        )
        {
            Calls.Add(broadcasterId);
            return Task.CompletedTask;
        }
    }

    private static (
        SongRequestQueueReconciler Sut,
        FairQueue<SongRequestEntry> Queue,
        RecordingEventBus Bus,
        SongRequestQueueStore Store,
        RecordingHandover Handover
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
        RecordingHandover handover = new();
        return (
            new SongRequestQueueReconciler(
                store,
                handover,
                new NoOpSongRequestQueuePersistence(),
                bus
            ),
            queue,
            bus,
            store,
            handover
        );
    }
}
