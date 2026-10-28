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
using NomNomzBot.Domain.Music.Events;
using NomNomzBot.Infrastructure.Music;
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
                new NoOpSongRequestQueuePersistence(),
                Bus,
                PlaybackDirectorTestKit.Registry(Store, Bus)
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

    [Fact]
    public async Task Row1_SixteenRequests_SpotifyReportsTheSixteenth_OnlyThatRowLeavesAndItIsJournaled()
    {
        Rig rig = new();
        for (int n = 1; n <= 16; n++)
            rig.Request($"spotify:track:{n}", $"T{n}", $"viewer{n}");

        await rig.Report("spotify", "spotify:track:16");

        // Spotify plays, so the first request in fair order is lined up in its own queue.
        rig.Titles.Should().Equal(Enumerable.Range(1, 15).Select(n => $"T{n}"));
        rig.Reasons.Should().Equal("played_out_of_order", "lined_up");
    }

    [Fact]
    public async Task Row3_YoutubeHeadWaitsOnSpotifyOwnerMusic_TheSpotifyRequestIsLinedUpAndLeavesOnlyWhenItPlays()
    {
        Rig rig = new();
        string video = "https://www.youtube.com/watch?v=head";
        rig.Request(video, "YT head", "viewer-y");
        string owner = "spotify:track:owner";
        string later = "spotify:track:later";

        await rig.Report("spotify", owner, progressMs: 1_000);
        rig.Request(later, "Spotify request", "viewer-s");
        await rig.Report("spotify", owner, progressMs: 60_000);

        // The Spotify request is second, yet it goes into Spotify's own queue now (owner, 2026-10-19).
        rig.Titles.Should().Equal("YT head", "Spotify request");
        rig.Reasons.Should().Equal("owner_music", "lined_up");

        // The owner's track is in its last seconds: nothing is cut, and nothing has left.
        await rig.Report("spotify", owner, progressMs: 196_000);
        rig.Titles.Should().Equal("YT head", "Spotify request");
        rig.Reasons.Should().Equal("owner_music", "lined_up");

        // Spotify reaches the lined-up request: the video takes over and Spotify is paused on it.
        await rig.Report("spotify", later, progressMs: 500);
        await rig.Report("spotify", later, playing: false, progressMs: 900);
        await rig.Report("youtube", video);

        rig.Titles.Should().Equal("Spotify request");
        rig.Reasons.Should()
            .Equal("owner_music", "lined_up", "youtube_takeover", "stop_confirmed", "started");

        // The video ends: Spotify resumes the paused request, and only now does it leave.
        await rig.Report("youtube", video, playing: false, ended: true);
        await rig.Report("spotify", later, progressMs: 1_500);

        rig.Titles.Should().BeEmpty();
        rig.Reasons.Should().Contain("spotify_resumed");
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
