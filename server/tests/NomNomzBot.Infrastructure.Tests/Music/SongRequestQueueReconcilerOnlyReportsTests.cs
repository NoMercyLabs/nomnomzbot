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
    public async Task A_playback_change_with_nothing_in_flight_calls_no_handover_and_starts_nothing()
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
        FailingHandover handover = new();
        SongRequestQueueReconciler sut = new(
            store,
            handover,
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

        handover
            .Calls.Should()
            .BeEmpty("the reconciler reports; it never hands a song to an engine");
        spotify.Calls.Where(c => c.Name == "Start").Should().BeEmpty();
        rig.Registry.StateOf(ChannelId).Should().Be(PlaybackState.Idle);
        store
            .GetOrCreate(ChannelId.ToString())
            .GetSnapshot()
            .Select(e => e.Item.TrackUri)
            .Should()
            .Equal("spotify:track:w1");
        store.GetInFlight(ChannelId.ToString()).Should().BeNull();
    }

    /// <summary>A handover double that records every call; the test fails when any call arrives.</summary>
    private sealed class FailingHandover : ISongRequestHandover
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
}
