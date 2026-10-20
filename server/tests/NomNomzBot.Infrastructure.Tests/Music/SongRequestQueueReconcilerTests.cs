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
using NomNomzBot.Application.Music.Services;
using NomNomzBot.Domain.Music.Events;
using NomNomzBot.Domain.Music.ValueObjects;
using NomNomzBot.Infrastructure.Music;
using NomNomzBot.Infrastructure.Music.Playback;
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
            new NoOpSongRequestQueuePersistence(),
            bus,
            PlaybackDirectorTestKit.Registry(store, bus)
        );

        await sut.HandleAsync(PlaybackOf("spotify:track:a"));

        bus.Published.OfType<SongRequestQueueChangedEvent>()
            .Single()
            .Items.Should()
            .ContainSingle(i => i.Title == "Track b" && i.Code == "P9WT");
    }

    [Fact]
    public async Task The_handed_over_track_starting_frees_the_provider_and_lines_up_the_next_request_in_Spotify_at_once()
    {
        (
            SongRequestQueueReconciler sut,
            FairQueue<SongRequestEntry> queue,
            RecordingEventBus bus,
            SongRequestQueueStore store,
            PlaybackDirectorTestKit.FakeEngineFactory engines
        ) = Build("a", "b", "c");
        HandOver(store, queue, "a");

        await sut.HandleAsync(PlaybackOf("spotify:track:a"));

        Titles(queue).Should().Equal("Track b", "Track c");
        // "a" started, so the provider is free and "a" is now the playing track. The director starts
        // nothing; it lines up exactly one request, "b", in Spotify's own queue.
        store.GetInFlight(ChannelId.ToString()).Should().BeNull("the provider is free again");
        StartedNothing(engines);
        LinedUp(engines).Should().Equal("spotify:track:b|viewer-b");
        bus.Published.OfType<SongRequestQueueChangedEvent>()
            .Single()
            .Items.Select(i => i.Title)
            .Should()
            .Equal("Track b", "Track c");

        await sut.HandleAsync(PlaybackOf("spotify:track:a", trackEnded: true));

        // At the end of "a" Spotify plays "b" from its own queue: no start, and no second line-up.
        StartedNothing(engines);
        LinedUp(engines).Should().Equal("spotify:track:b|viewer-b");
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

        PlayingRequester
            .Of(store, PlaybackState.Idle, ChannelId.ToString(), "spotify:track:a")
            .Should()
            .NotBeNullOrEmpty("the handed-over request is now playing and keeps its requester");

        await sut.HandleAsync(PlaybackOf("spotify:track:streamer-playlist"));

        PlayingRequester
            .Of(store, PlaybackState.Idle, ChannelId.ToString(), "spotify:track:streamer-playlist")
            .Should()
            .BeNull("a track nobody requested carries no requester");
        PlayingRequester
            .Of(store, PlaybackState.Idle, ChannelId.ToString(), "spotify:track:a")
            .Should()
            .BeNull("the memory ends once another track plays");
    }

    [Fact]
    public async Task A_track_that_is_not_ours_while_one_is_handed_over_lines_up_exactly_one_request()
    {
        (
            SongRequestQueueReconciler sut,
            FairQueue<SongRequestEntry> queue,
            _,
            SongRequestQueueStore store,
            PlaybackDirectorTestKit.FakeEngineFactory engines
        ) = Build("a", "b");
        HandOver(store, queue, "a");

        // The streamer's own playlist track is playing. Spotify gets exactly ONE request in its own queue,
        // the head "a"; a second one would freeze their order at the provider.
        await sut.HandleAsync(PlaybackOf("spotify:track:streamer-playlist"));

        StartedNothing(engines);
        LinedUp(engines).Should().Equal("spotify:track:a|viewer-a");
        Titles(queue).Should().Equal("Track a", "Track b");
        store.GetInFlight(ChannelId.ToString())!.TrackName.Should().Be("Track a");
    }

    [Fact]
    public async Task A_crossfade_that_starts_the_song_after_the_sent_one_removes_only_that_song()
    {
        (
            SongRequestQueueReconciler sut,
            FairQueue<SongRequestEntry> queue,
            _,
            SongRequestQueueStore store,
            PlaybackDirectorTestKit.FakeEngineFactory engines
        ) = Build("a", "b", "c");
        HandOver(store, queue, "a");

        // Playback is observed on "b" without "a" ever being seen as current — a 12s crossfade
        // overlapping the poll cadence. Only the director removes rows, and it removes the one row it
        // saw play: "a" stays until it is seen playing too, and nothing new goes to the provider.
        await sut.HandleAsync(PlaybackOf("spotify:track:b"));

        Titles(queue).Should().Equal("Track a", "Track c");
        store.GetInFlight(ChannelId.ToString())!.TrackName.Should().Be("Track a");
        StartedNothing(engines);
        LinedUp(engines).Should().Equal("spotify:track:a|viewer-a");
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
            persistence,
            bus,
            PlaybackDirectorTestKit.Registry(store, bus)
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
    public async Task A_youtube_item_that_is_not_playing_stays_and_the_spotify_song_that_then_starts_removes_only_itself()
    {
        SongRequestQueueStore store = new();
        FairQueue<SongRequestEntry> queue = store.GetOrCreate(ChannelId.ToString());
        queue.Enqueue("viewer-y", Entry("https://www.youtube.com/watch?v=bad", "Broken video"));
        foreach (char id in "bcdefghijklmnop")
            queue.Enqueue($"viewer-{id}", Entry($"spotify:track:{id}", $"Track {id}"));
        store.SetInFlight(ChannelId.ToString(), queue.GetSnapshot()[0].Item);
        RecordingEventBus bus = new();
        PlaybackDirectorTestKit.FakeEngineFactory engines = new();
        RecordingPersistence persistence = new();
        SongRequestQueueReconciler sut = new(
            store,
            persistence,
            bus,
            new PlaybackDirectorRegistry(
                store,
                engines,
                bus,
                TimeProvider.System,
                new NomNomzBot.Infrastructure.Platform.Security.OutboundSanctionAccessor()
            )
        );

        // The overlay player reports the video as not playing: a report that does not play a row removes
        // nothing. Dropping a broken item is the director's two-failed-starts rule, not the adapter's.
        await sut.HandleAsync(
            PlaybackOf("https://www.youtube.com/watch?v=bad", "youtube", isPlaying: false)
        );

        Titles(queue)
            .Should()
            .Equal(["Broken video", .. "bcdefghijklmnop".Select(c => $"Track {c}")]);
        StartedNothing(engines);
        persistence.Synced.Should().BeEmpty();

        // Spotify then starts the 16th request on its own: only that row leaves.
        await sut.HandleAsync(PlaybackOf("spotify:track:p"));

        Titles(queue)
            .Should()
            .Equal(["Broken video", .. "bcdefghijklmno".Select(c => $"Track {c}")]);
        persistence.Synced.Should().HaveCount(15);
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
    public async Task Only_one_copy_of_the_track_that_just_started_leaves_the_queue()
    {
        (
            SongRequestQueueReconciler sut,
            FairQueue<SongRequestEntry> queue,
            _,
            SongRequestQueueStore store,
            PlaybackDirectorTestKit.FakeEngineFactory engines
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

        // The director removes the one row it saw play. The late copy (queued before the duplicate gate
        // existed) is its own request and stays; the gate keeps new duplicates out.
        Titles(queue).Should().Equal("Track b", "Track a");
        queue
            .GetSnapshot()
            .Select(e => e.Item.RequestedBy)
            .Should()
            .Equal("viewer-b", "viewer-late");
        store.GetInFlight(ChannelId.ToString()).Should().BeNull();
        StartedNothing(engines);
        LinedUp(engines).Should().Equal("spotify:track:b|viewer-b");

        await sut.HandleAsync(PlaybackOf("spotify:track:a", trackEnded: true));

        // The head, "b", was lined up once while the copy played; its end sends nothing more.
        StartedNothing(engines);
        LinedUp(engines).Should().Equal("spotify:track:b|viewer-b");
    }

    /// <summary>The Spotify poll hands its observation to the engine itself, so the reconciler must not report the
    /// same observation to the director a second time.</summary>
    [Theory]
    [InlineData(true, new[] { "spotify:track:a", "spotify:track:b" })]
    [InlineData(false, new[] { "spotify:track:b" })]
    public async Task An_engine_fed_poll_event_is_not_reported_to_the_director_again(
        bool fedToEngine,
        string[] expectedQueue
    )
    {
        SongRequestQueueStore store = new();
        FairQueue<SongRequestEntry> queue = store.GetOrCreate(ChannelId.ToString());
        queue.Enqueue("viewer-a", Entry("spotify:track:a", "Track a"));
        queue.Enqueue("viewer-b", Entry("spotify:track:b", "Track b"));
        RecordingEventBus bus = new();
        PlaybackDirectorTestKit.FakeEngineFactory engines = new();
        PlaybackDirectorRegistry registry = new(store, engines, bus, TimeProvider.System);
        await registry.SendAsync(ChannelId, new RequestAdded());
        SongRequestQueueReconciler sut = new(
            store,
            new NoOpSongRequestQueuePersistence(),
            bus,
            registry
        );

        await sut.HandleAsync(PlaybackOf("spotify:track:a", fedToEngine: fedToEngine));

        queue.GetSnapshot().Select(e => e.Item.TrackUri).Should().Equal(expectedQueue);
    }

    /// <summary>A play-once end whose resume is pending is kept from the director on purpose; an unfed event
    /// without that mark is still reported.</summary>
    [Theory]
    [InlineData(true, new[] { "spotify:track:a", "spotify:track:b" })]
    [InlineData(false, new[] { "spotify:track:b" })]
    public async Task An_end_withheld_for_a_pending_resume_is_not_reported_to_the_director(
        bool reportWithheld,
        string[] expectedQueue
    )
    {
        SongRequestQueueStore store = new();
        FairQueue<SongRequestEntry> queue = store.GetOrCreate(ChannelId.ToString());
        queue.Enqueue("viewer-a", Entry("spotify:track:a", "Track a"));
        queue.Enqueue("viewer-b", Entry("spotify:track:b", "Track b"));
        RecordingEventBus bus = new();
        PlaybackDirectorTestKit.FakeEngineFactory engines = new();
        PlaybackDirectorRegistry registry = new(store, engines, bus, TimeProvider.System);
        await registry.SendAsync(ChannelId, new RequestAdded());
        SongRequestQueueReconciler sut = new(
            store,
            new NoOpSongRequestQueuePersistence(),
            bus,
            registry
        );

        await sut.HandleAsync(PlaybackOf("spotify:track:a", reportWithheld: reportWithheld));

        queue.GetSnapshot().Select(e => e.Item.TrackUri).Should().Equal(expectedQueue);
    }

    private static PlaybackStateChangedEvent PlaybackOf(
        string trackUri,
        string provider = "spotify",
        bool isPlaying = true,
        bool fedToEngine = false,
        bool reportWithheld = false,
        bool trackEnded = false
    ) =>
        new()
        {
            TrackEnded = trackEnded,
            FedToEngine = fedToEngine,
            ReportWithheld = reportWithheld,
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

    /// <summary>
    /// The director started no song on either engine of the channel. Lining one request up in Spotify's own
    /// queue is not a start: Spotify plays it by itself (owner, 2026-10-19).
    /// </summary>
    private static void StartedNothing(PlaybackDirectorTestKit.FakeEngineFactory engines)
    {
        engines
            .EngineOf(ChannelId, EngineKind.Spotify)
            .Calls.Should()
            .NotContain(c => c.Name == "Start");
        engines.EngineOf(ChannelId, EngineKind.YouTube).Calls.Should().BeEmpty();
    }

    /// <summary>The requests the director lined up in Spotify's own queue, in order.</summary>
    private static IEnumerable<string?> LinedUp(
        PlaybackDirectorTestKit.FakeEngineFactory engines
    ) =>
        engines
            .EngineOf(ChannelId, EngineKind.Spotify)
            .Calls.Where(c => c.Name == "QueueNext")
            .Select(c => c.Argument);

    private static (
        SongRequestQueueReconciler Sut,
        FairQueue<SongRequestEntry> Queue,
        RecordingEventBus Bus,
        SongRequestQueueStore Store,
        PlaybackDirectorTestKit.FakeEngineFactory Engines
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
        PlaybackDirectorTestKit.FakeEngineFactory engines = new();
        return (
            new(
                store,
                new NoOpSongRequestQueuePersistence(),
                bus,
                new PlaybackDirectorRegistry(
                    store,
                    engines,
                    bus,
                    TimeProvider.System,
                    new NomNomzBot.Infrastructure.Platform.Security.OutboundSanctionAccessor()
                )
            ),
            queue,
            bus,
            store,
            engines
        );
    }
}
