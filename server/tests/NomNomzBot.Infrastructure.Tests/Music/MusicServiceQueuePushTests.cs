// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Net;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Infrastructure.Integrations;
using NomNomzBot.Infrastructure.Music;
using NomNomzBot.Infrastructure.Tests.Identity;

namespace NomNomzBot.Infrastructure.Tests.Music;

/// <summary>
/// Exactly ONE request is handed to the provider at a time. That is what makes the fair queue real: the
/// tracks behind the current one stay in OUR queue, where a viewer's first request can still be re-ranked
/// ahead of someone else's third. Handing the whole queue over would freeze the order at arrival time and
/// reduce the fair queue to decoration. The reconciler hands the next one over when playback moves on;
/// what this file pins is the request path's half — push exactly one, queue the rest, and never report a
/// failed push as a success.
/// </summary>
public sealed class MusicServiceQueuePushTests
{
    private static readonly Guid ChannelId = Guid.Parse("0192a000-0000-7000-8000-000000007801");

    /// <summary>Each search answers with a DIFFERENT track, so these tests exercise the push path rather
    /// than tripping the duplicate gate (which has its own tests).</summary>
    private static string SearchJson(string id) =>
        """
            {"tracks":{"items":[{"name":"Song __ID__","uri":"spotify:track:__ID__","duration_ms":200000,"artists":[{"name":"Artist"}],"album":{"name":"Album","images":[]}}]}}
            """.Replace("__ID__", id);

    [Fact]
    public async Task Only_the_first_request_reaches_the_provider_the_rest_wait_in_the_fair_queue()
    {
        (MusicService sut, RecordingHttpHandler handler) = Build();

        Result first = await RequestAsync(sut, handler, "a1", "viewer1");
        Result second = await RequestAsync(sut, handler, "a2", "viewer2");
        Result third = await RequestAsync(sut, handler, "a3", "viewer3");

        first.IsSuccess.Should().BeTrue();
        second.IsSuccess.Should().BeTrue();
        third.IsSuccess.Should().BeTrue();
        QueuePushCount(handler)
            .Should()
            .Be(1, "only the track now waiting to play belongs at the provider");
        // All three are accepted and ordered by US — and the order is the distinction that matters, not
        // the count: three separate viewers each get rank 1, so arrival order holds here.
        (await sut.GetQueueAsync(ChannelId.ToString()))
            .Queue.Select(i => i.TrackName)
            .Should()
            .Equal("Song a1", "Song a2", "Song a3");
    }

    [Fact]
    public async Task A_failed_push_takes_only_that_request_back_out_and_lets_the_next_one_through()
    {
        (MusicService sut, RecordingHttpHandler handler) = Build();

        // Nothing is at the provider yet, so this request is the one that gets pushed — and the push fails.
        handler.ClearRoutes();
        handler.RespondWhen(
            r => r.RequestUri!.AbsolutePath.EndsWith("/search", StringComparison.Ordinal),
            HttpStatusCode.OK,
            SearchJson("b1")
        );
        handler.RespondWhen(
            r =>
                r.Method == HttpMethod.Post
                && r.RequestUri!.AbsolutePath.EndsWith(
                    "/me/player/queue",
                    StringComparison.Ordinal
                ),
            HttpStatusCode.NotFound,
            """{"error":{"status":404,"reason":"NO_ACTIVE_DEVICE","message":"No active device"}}"""
        );

        Result failed = await sut.AddToQueueAsync(ChannelId.ToString(), "song b1", "viewer1");

        failed.ErrorCode.Should().Be("NO_ACTIVE_DEVICE");
        (await sut.GetQueueAsync(ChannelId.ToString()))
            .Queue.Should()
            .BeEmpty(
                "a request that never reached the provider is not left behind pretending to be live"
            );

        // Nothing was left marked as in-flight either, so the next request is still handed over.
        Result recovered = await RequestAsync(sut, handler, "b2", "viewer1");

        recovered.IsSuccess.Should().BeTrue();
        (await sut.GetQueueAsync(ChannelId.ToString()))
            .Queue.Select(i => i.TrackName)
            .Should()
            .Equal("Song b2");
    }

    private static int QueuePushCount(RecordingHttpHandler handler) =>
        handler.RequestUrls.Count(url =>
            url.Contains("/me/player/queue", StringComparison.Ordinal)
        );

    /// <summary>Points search at one specific track, then makes the request — the handler is first-match-
    /// wins, so the search route is re-registered per call to hand back a distinct track each time.</summary>
    private static async Task<Result> RequestAsync(
        MusicService sut,
        RecordingHttpHandler handler,
        string trackId,
        string requestedBy
    )
    {
        handler.ClearRoutes();
        handler.RespondWhen(
            r => r.RequestUri!.AbsolutePath.EndsWith("/search", StringComparison.Ordinal),
            HttpStatusCode.OK,
            SearchJson(trackId)
        );
        RespondQueuePush(handler, HttpStatusCode.NoContent);
        return await sut.AddToQueueAsync(ChannelId.ToString(), $"song {trackId}", requestedBy);
    }

    private static void RespondQueuePush(RecordingHttpHandler handler, HttpStatusCode queueStatus)
    {
        handler.RespondWhen(
            r =>
                r.Method == HttpMethod.Post
                && r.RequestUri!.AbsolutePath.EndsWith(
                    "/me/player/queue",
                    StringComparison.Ordinal
                ),
            queueStatus
        );
    }

    private static (MusicService Sut, RecordingHttpHandler Handler) Build()
    {
        MusicTestDbContext db = new(
            new DbContextOptionsBuilder<MusicTestDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options
        );
        db.Services.Add(
            new()
            {
                Id = Guid.NewGuid().ToString(),
                Name = "spotify",
                BroadcasterId = ChannelId,
                Enabled = true,
                AccessToken = "test-access-token",
            }
        );
        db.SaveChanges();

        FakeIntegrationTokenVault vault = new(db);
        vault.SeedConnectedSpotify(ChannelId);

        RecordingHttpHandler handler = new();
        SpotifyMusicProvider spotify = new(
            db,
            vault,
            new InMemoryIntegrationCapabilityStore(),
            new LastActiveSpotifyDeviceTracker(),
            new SingleHandlerClientFactory(handler),
            TimeProvider.System,
            NullLogger<SpotifyMusicProvider>.Instance,
            NullSystemCredentialsProvider.Instance
        );

        MusicService sut = new(
            [spotify],
            db,
            new RecordingEventBus(),
            new BlockedTrackService(db),
            new SongRequestQueueStore(),
            new NoOpSongRequestQueuePersistence(),
            NullLogger<MusicService>.Instance,
            new InMemoryIntegrationCapabilityStore()
        );
        return (sut, handler);
    }
}
