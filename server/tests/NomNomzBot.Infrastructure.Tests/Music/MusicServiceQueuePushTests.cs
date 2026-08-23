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
/// Every accepted song request must reach the provider's own queue — not just ours. The fair queue is a
/// display/ordering mirror of what is pending; Spotify is what actually plays the audio. An earlier
/// version only pushed when our queue held one entry ("nothing ahead of it"), which silently worked
/// while the queue reset every DI scope and broke the moment the queue became a real singleton: from
/// the second request onward nothing ever reached Spotify and requests only accumulated locally.
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
    public async Task Every_request_is_pushed_to_the_provider_queue_not_only_the_first()
    {
        (MusicService sut, RecordingHttpHandler handler) = Build();
        RespondQueuePush(handler, HttpStatusCode.NoContent);

        Result first = await RequestAsync(sut, handler, "a1", "viewer1");
        Result second = await RequestAsync(sut, handler, "a2", "viewer2");
        Result third = await RequestAsync(sut, handler, "a3", "viewer3");

        first.IsSuccess.Should().BeTrue();
        second.IsSuccess.Should().BeTrue();
        third.IsSuccess.Should().BeTrue();
        QueuePushCount(handler).Should().Be(3);
        (await sut.GetQueueAsync(ChannelId.ToString())).Queue.Should().HaveCount(3);
    }

    [Fact]
    public async Task A_failed_push_on_a_later_request_rolls_back_only_that_request()
    {
        (MusicService sut, RecordingHttpHandler handler) = Build();
        RespondQueuePush(handler, HttpStatusCode.NoContent);

        await RequestAsync(sut, handler, "b1", "viewer1");
        await RequestAsync(sut, handler, "b2", "viewer1");

        // The next push fails with Spotify's no-active-device reason (routes are first-match-wins, so
        // the success route has to go before the failure one can answer).
        handler.ClearRoutes();
        handler.RespondWhen(
            r => r.RequestUri!.AbsolutePath.EndsWith("/search", StringComparison.Ordinal),
            HttpStatusCode.OK,
            SearchJson("b3")
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

        Result failed = await sut.AddToQueueAsync(ChannelId.ToString(), "song b3", "viewer1");

        failed.ErrorCode.Should().Be("NO_ACTIVE_DEVICE");
        // Only the rejected entry is rolled back — the viewer's two accepted requests survive.
        (await sut.GetQueueAsync(ChannelId.ToString()))
            .Queue.Should()
            .HaveCount(2);
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
            NullLogger<MusicService>.Instance,
            new InMemoryIntegrationCapabilityStore()
        );
        return (sut, handler);
    }
}
