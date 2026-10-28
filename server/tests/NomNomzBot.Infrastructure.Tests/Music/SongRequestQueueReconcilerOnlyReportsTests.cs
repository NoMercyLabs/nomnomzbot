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
using NomNomzBot.Domain.Music.ValueObjects;
using NomNomzBot.Infrastructure.Music;
using NomNomzBot.Infrastructure.Tests.Identity;
using static NomNomzBot.Infrastructure.Tests.Music.PlaybackDirectorTestKit;

namespace NomNomzBot.Infrastructure.Tests.Music;

/// <summary>
/// The queue reconciler only reports what plays. It never takes the queue head to an engine: starting a song
/// is the playback director's job alone.
/// </summary>
public sealed class SongRequestQueueReconcilerOnlyReportsTests
{
    private static readonly Guid ChannelId = Guid.Parse("0192a000-0000-7000-8000-000000007a01");

    [Fact]
    public async Task A_playback_change_with_nothing_in_flight_starts_nothing_and_leaves_the_queue_alone()
    {
        SongRequestQueueStore store = new();
        store
            .GetOrCreate(ChannelId.ToString())
            .Enqueue(
                "viewer-w1",
                new(
                    "spotify:track:w1",
                    "Song w1",
                    "Artist",
                    null,
                    200_000,
                    "viewer-w1",
                    0,
                    null,
                    ""
                )
            );
        DirectorRig rig = DirectorRig.Of(store);
        FakeEngine spotify = rig.Engines.EngineOf(ChannelId, EngineKind.Spotify);
        RecordingEventBus bus = new();
        SongRequestQueueReconciler sut = new(
            store,
            new NoOpSongRequestQueuePersistence(),
            bus,
            rig.Registry
        );

        await sut.HandleAsync(
            new PlaybackStateChangedEvent
            {
                FedToEngine = true,
                BroadcasterId = ChannelId,
                IsPlaying = true,
                TrackUri = "spotify:track:other",
                TrackName = "Other",
                Provider = "spotify",
                ObservedAt = DateTimeOffset.UtcNow,
            }
        );

        spotify
            .Calls.Where(c => c.Name == "Start")
            .Should()
            .BeEmpty("the reconciler reports; it never hands a song to an engine");
        rig.Registry.StateOf(ChannelId).Should().Be(PlaybackState.Idle);
        store
            .GetOrCreate(ChannelId.ToString())
            .GetSnapshot()
            .Select(e => e.Item.TrackUri)
            .Should()
            .Equal("spotify:track:w1");
        store.GetInFlight(ChannelId.ToString()).Should().BeNull();
    }
}
