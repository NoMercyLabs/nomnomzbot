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
using NomNomzBot.Infrastructure.Music.Playback;
using NomNomzBot.Infrastructure.Tests.Identity;

namespace NomNomzBot.Infrastructure.Tests.Music;

/// <summary>
/// The reconciler is a report adapter: it passes what the player reported to the playback director and
/// never removes a row itself. These tests run the real fair queue through the real director.
/// </summary>
public sealed class SongRequestQueueReportAdapterTests
{
    private static readonly Guid ChannelId = Guid.Parse("0192a000-0000-7000-8000-000000007902");

    private sealed class Rig
    {
        public Rig()
        {
            Queue = Store.GetOrCreate(ChannelId.ToString());
            Reconciler = new(
                Store,
                new NoHandover(),
                new NoOpSongRequestQueuePersistence(),
                Bus,
                new PlaybackDirectorReportSink(Store, Bus, TimeProvider.System)
            );
        }

        public SongRequestQueueStore Store { get; } = new();

        public RecordingEventBus Bus { get; } = new();

        public FairQueue<SongRequestEntry> Queue { get; }

        public SongRequestQueueReconciler Reconciler { get; }

        public List<string> Titles => [.. Queue.GetSnapshot().Select(e => e.Item.TrackName)];

        public List<string> Reasons =>
            [.. Bus.Published.OfType<PlaybackTransitionedEvent>().Select(e => e.Reason)];

        public void Request(string uri, string title, string viewer) =>
            Queue.Enqueue(
                viewer,
                new(uri, title, "Artist", null, 200_000, viewer, 0, null, $"C-{title}")
            );

        public Task Report(
            string provider,
            string uri,
            bool playing = true,
            int progressMs = 1_000,
            bool ended = false
        ) =>
            Reconciler.HandleAsync(
                new PlaybackStateChangedEvent
                {
                    BroadcasterId = ChannelId,
                    IsPlaying = playing,
                    TrackUri = uri,
                    Provider = provider,
                    ProgressMs = progressMs,
                    DurationMs = 200_000,
                    TrackEnded = ended,
                    ObservedAt = DateTimeOffset.UtcNow,
                }
            );
    }

    private sealed class NoHandover : ISongRequestHandover
    {
        public Task HandOverNextAsync(
            string broadcasterId,
            CancellationToken cancellationToken = default
        ) => Task.CompletedTask;
    }

    [Fact]
    public async Task Row1_SixteenRequests_SpotifyReportsTheSixteenth_OnlyThatRowLeavesAndItIsJournaled()
    {
        Rig rig = new();
        for (int n = 1; n <= 16; n++)
            rig.Request($"spotify:track:{n}", $"T{n}", $"viewer{n}");

        await rig.Report("spotify", "spotify:track:16");

        rig.Titles.Should().Equal(Enumerable.Range(1, 15).Select(n => $"T{n}"));
        rig.Reasons.Should().Equal("played_out_of_order");
    }

    [Fact]
    public async Task Row3_YoutubeHeadWaitsOnSpotifyOwnerMusic_ASpotifyRequestStaysSecond_NothingLeavesEarly()
    {
        Rig rig = new();
        rig.Request("https://www.youtube.com/watch?v=head", "YT head", "viewer-y");
        string owner = "spotify:track:owner";

        await rig.Report("spotify", owner, progressMs: 1_000);
        rig.Request("spotify:track:later", "Spotify request", "viewer-s");
        await rig.Report("spotify", owner, progressMs: 60_000);

        rig.Titles.Should().Equal("YT head", "Spotify request");
        rig.Reasons.Should().Equal("owner_music");

        // The owner's track is in its last seconds: the head takes over, and still nothing has left.
        await rig.Report("spotify", owner, progressMs: 196_000);
        rig.Titles.Should().Equal("YT head", "Spotify request");
        rig.Reasons.Should().Equal("owner_music", "hand_over");

        // Spotify goes quiet, then the YouTube head plays: only the head leaves.
        await rig.Report("spotify", owner, playing: false, progressMs: 199_000);
        await rig.Report("youtube", "https://www.youtube.com/watch?v=head");

        rig.Titles.Should().Equal("Spotify request");
        rig.Reasons.Should().Equal("owner_music", "hand_over", "stop_confirmed", "started");
    }

    [Fact]
    public async Task A_track_end_is_passed_on_and_removes_nothing_from_the_queue()
    {
        Rig rig = new();
        rig.Request("spotify:track:a", "A", "viewer-a");
        rig.Request("spotify:track:b", "B", "viewer-b");
        await rig.Report("spotify", "spotify:track:a");
        rig.Titles.Should().Equal("B");

        await rig.Report("spotify", "spotify:track:a", playing: false, ended: true);

        rig.Titles.Should().Equal("B");
    }
}
