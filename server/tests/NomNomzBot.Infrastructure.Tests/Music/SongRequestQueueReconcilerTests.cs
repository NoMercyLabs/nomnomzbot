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
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Music.Services;
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

    /// <summary>
    /// S-MUSIC-4c: the reconciler's own snapshot publish used to construct <see cref="SongRequestQueueSnapshotItem"/>
    /// with only 3 positional args, so <c>Code</c> silently defaulted to "" and the <c>sr_queue</c> overlay never
    /// saw a real code for a request that fell out of the queue via the reconciler path (as opposed to
    /// <see cref="MusicService"/>'s own add/remove publish, which had the same bug at its own call site).
    /// </summary>
    [Fact]
    public async Task Queue_changed_snapshot_carries_each_remaining_entrys_real_code()
    {
        SongRequestQueueStore store = new();
        FairQueue<SongRequestEntry> queue = store.GetOrCreate(ChannelId.ToString());
        queue.Enqueue(
            "viewer-a",
            new("spotify:track:a", "Track a", "Artist", null, 200000, "viewer-a", 0, null, "K7QM")
        );
        queue.Enqueue(
            "viewer-b",
            new("spotify:track:b", "Track b", "Artist", null, 200000, "viewer-b", 0, null, "P9WT")
        );
        store.SetInFlight(
            ChannelId.ToString(),
            queue.GetSnapshot().Single(e => e.Item.TrackUri == "spotify:track:a").Item
        );
        RecordingEventBus bus = new();
        SongRequestQueueReconciler sut = new(
            store,
            new RecordingHandover(),
            new NoOpSongRequestQueuePersistence(),
            bus,
            NullLogger<SongRequestQueueReconciler>.Instance
        );

        await sut.HandleAsync(PlaybackOf("spotify:track:a"));

        bus.Published.OfType<SongRequestQueueChangedEvent>()
            .Single()
            .Items.Should()
            .ContainSingle(i => i.Title == "Track b" && i.Code == "P9WT");
    }

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
    public async Task Queue_changed_snapshot_numbers_the_remaining_entries_the_way_the_queue_command_does()
    {
        (
            SongRequestQueueReconciler sut,
            FairQueue<SongRequestEntry> queue,
            RecordingEventBus bus,
            SongRequestQueueStore store,
            _
        ) = Build("a", "b", "c");
        HandOver(store, queue, "a");

        await sut.HandleAsync(PlaybackOf("spotify:track:a"));

        SongRequestQueueSnapshotItem[] items = bus
            .Published.OfType<SongRequestQueueChangedEvent>()
            .Single()
            .Items.ToArray();
        items.Select(i => i.Title).Should().Equal("Track b", "Track c");
        items
            .Select(i => i.Number)
            .Should()
            .Equal(QueueNumbering.NumberOf(0), QueueNumbering.NumberOf(1));
        items.Select(i => i.Number).Should().Equal(1, 2);
    }

    [Fact]
    public async Task The_requester_of_the_playing_track_is_remembered_after_it_leaves_the_queue()
    {
        (
            SongRequestQueueReconciler sut,
            FairQueue<SongRequestEntry> queue,
            _,
            SongRequestQueueStore store,
            _
        ) = Build("a", "b");
        HandOver(store, queue, "a");

        await sut.HandleAsync(PlaybackOf("spotify:track:a"));

        MusicService
            .RequesterOfPlayingTrack(store, ChannelId.ToString(), "spotify:track:a")
            .Should()
            .NotBeNullOrEmpty("the handed-over request is now playing and keeps its requester");

        await sut.HandleAsync(PlaybackOf("spotify:track:streamer-playlist"));

        MusicService
            .RequesterOfPlayingTrack(store, ChannelId.ToString(), "spotify:track:streamer-playlist")
            .Should()
            .BeNull("a track nobody requested carries no requester");
        MusicService
            .RequesterOfPlayingTrack(store, ChannelId.ToString(), "spotify:track:a")
            .Should()
            .BeNull("the memory ends once another track plays");
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
    public async Task A_crossfade_that_starts_the_song_after_the_sent_one_drops_both()
    {
        (
            SongRequestQueueReconciler sut,
            FairQueue<SongRequestEntry> queue,
            _,
            SongRequestQueueStore store,
            RecordingHandover handover
        ) = Build("a", "b", "c");
        HandOver(store, queue, "a");

        // Playback is observed on "b" without "a" ever being seen as current — a 12s crossfade
        // overlapping the poll cadence.
        await sut.HandleAsync(PlaybackOf("spotify:track:b"));

        Titles(queue).Should().Equal("Track c");
        store.GetInFlight(ChannelId.ToString()).Should().BeNull();
        handover.Calls.Should().HaveCount(1);
    }

    [Fact]
    public async Task A_song_deep_in_the_queue_that_starts_removes_only_itself()
    {
        (
            SongRequestQueueReconciler sut,
            FairQueue<SongRequestEntry> queue,
            RecordingEventBus bus,
            SongRequestQueueStore store,
            _
        ) = Build("a", "b", "c", "d", "e", "f", "g", "h", "i", "j", "k", "l", "m", "n", "o", "p");
        HandOver(store, queue, "a");
        RecordingPersistence persistence = new();
        SongRequestQueueReconciler withPersistence = new(
            store,
            new RecordingHandover(),
            persistence,
            bus,
            NullLogger<SongRequestQueueReconciler>.Instance
        );

        // The 16th request starts in Spotify while the first is still the one we sent.
        await withPersistence.HandleAsync(PlaybackOf("spotify:track:p"));

        string[] expected = [.. "abcdefghijklmno".Select(c => $"Track {c}")];
        Titles(queue).Should().Equal(expected);
        persistence.Synced.Select(e => e.Item.TrackName).Should().Equal(expected);
        bus.Published.OfType<SongRequestQueueChangedEvent>()
            .Single()
            .Items.Select(i => i.Title)
            .Should()
            .Equal(expected.Take(10));
        store.GetInFlight(ChannelId.ToString())!.TrackName.Should().Be("Track a");
    }

    [Fact]
    public async Task A_failed_youtube_item_leaves_alone_and_the_spotify_song_that_then_starts_removes_only_itself()
    {
        SongRequestQueueStore store = new();
        FairQueue<SongRequestEntry> queue = store.GetOrCreate(ChannelId.ToString());
        queue.Enqueue("viewer-y", Entry("https://www.youtube.com/watch?v=bad", "Broken video"));
        foreach (char id in "bcdefghijklmnop")
            queue.Enqueue($"viewer-{id}", Entry($"spotify:track:{id}", $"Track {id}"));
        store.SetInFlight(ChannelId.ToString(), queue.GetSnapshot()[0].Item);
        RecordingEventBus bus = new();
        RecordingHandover handover = new();
        RecordingPersistence persistence = new();
        SongRequestQueueReconciler sut = new(
            store,
            handover,
            persistence,
            bus,
            NullLogger<SongRequestQueueReconciler>.Instance
        );

        // The overlay player reports Error for the video: only that request leaves, the next is handed over.
        await sut.HandleAsync(
            PlaybackOf("https://www.youtube.com/watch?v=bad", "youtube", isPlaying: false)
        );

        Titles(queue).Should().Equal("bcdefghijklmnop".Select(c => $"Track {c}"));
        handover.Calls.Should().ContainSingle();
        store.GetInFlight(ChannelId.ToString()).Should().BeNull();

        // Spotify then starts its own queue head, which is the last request (nothing is in flight yet).
        store.SetInFlight(ChannelId.ToString(), queue.GetSnapshot()[0].Item);
        await sut.HandleAsync(PlaybackOf("spotify:track:p"));

        Titles(queue).Should().Equal("bcdefghijklmno".Select(c => $"Track {c}"));
        persistence.Synced.Should().HaveCount(14);
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

    [Fact]
    public async Task Every_copy_of_the_track_that_just_started_leaves_the_queue()
    {
        (
            SongRequestQueueReconciler sut,
            FairQueue<SongRequestEntry> queue,
            _,
            SongRequestQueueStore store,
            RecordingHandover handover
        ) = Build("a", "b");
        // A second viewer had already queued track "a" before the duplicate gate existed, so the
        // persisted queue carries two copies of it.
        queue.Enqueue(
            "viewer-late",
            new("spotify:track:a", "Track a", "Artist", null, 200000, "viewer-late", 0, null, "")
        );
        // In-flight is the FIRST copy — the one actually handed to the provider.
        store.SetInFlight(
            ChannelId.ToString(),
            queue.GetSnapshot().First(e => e.Item.TrackUri == "spotify:track:a").Item
        );

        await sut.HandleAsync(PlaybackOf("spotify:track:a"));

        // Only "b" may remain. Leaving the second copy behind put it straight back at the head, so the
        // track that had just played was handed over again on the next tick — the same song forever.
        Titles(queue).Should().Equal("Track b");
        store.GetInFlight(ChannelId.ToString()).Should().BeNull();
        handover.Calls.Should().ContainSingle();
    }

    private static PlaybackStateChangedEvent PlaybackOf(
        string trackUri,
        string provider = "spotify",
        bool isPlaying = true
    ) =>
        new()
        {
            BroadcasterId = ChannelId,
            IsPlaying = isPlaying,
            TrackUri = trackUri,
            TrackName = trackUri,
            Provider = provider,
            ObservedAt = DateTimeOffset.UtcNow,
        };

    private static SongRequestEntry Entry(string trackUri, string trackName) =>
        new(trackUri, trackName, "Artist", null, 200000, "viewer", 0, null, "");

    /// <summary>Keeps the last snapshot the reconciler wrote, i.e. the rows the table would hold.</summary>
    private sealed class RecordingPersistence : ISongRequestQueuePersistence
    {
        public IReadOnlyList<(SongRequestEntry Item, int Rank, string OwnerKey)> Synced
        {
            get;
            private set;
        } = [];

        public Task SyncAsync(
            string broadcasterId,
            IReadOnlyList<(SongRequestEntry Item, int Rank, string OwnerKey)> snapshot,
            CancellationToken cancellationToken,
            SongRequestEntry? inFlight = null
        )
        {
            Synced = snapshot;
            return Task.CompletedTask;
        }

        public Task<SongRequestQueueRestoreResult> LoadForRestoreAsync(
            TimeSpan freshnessWindow,
            CancellationToken cancellationToken
        ) => Task.FromResult(new SongRequestQueueRestoreResult([], []));
    }

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
                new(
                    $"spotify:track:{id}",
                    $"Track {id}",
                    "Artist",
                    null,
                    200000,
                    $"viewer-{id}",
                    0,
                    null,
                    ""
                )
            );

        RecordingEventBus bus = new();
        RecordingHandover handover = new();
        return (
            new(
                store,
                handover,
                new NoOpSongRequestQueuePersistence(),
                bus,
                NullLogger<SongRequestQueueReconciler>.Instance
            ),
            queue,
            bus,
            store,
            handover
        );
    }
}
