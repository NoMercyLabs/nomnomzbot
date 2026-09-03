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
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Economy.Services;
using NomNomzBot.Application.Music.Dtos;
using NomNomzBot.Application.Music.Services;
using NomNomzBot.Infrastructure.Identity;
using NomNomzBot.Infrastructure.Integrations;
using NomNomzBot.Infrastructure.Music;
using NomNomzBot.Infrastructure.Tests.Identity;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Music;

/// <summary>
/// Proves the <c>MaxQueueSize</c>/<c>MaxRequestsPerUser</c> capacity gate inside
/// <see cref="MusicService"/>'s shared enqueue point (S067): a channel at its configured queue cap
/// refuses the next request with <c>QUEUE_FULL</c>, and a requester who already owns their configured
/// share of the queue is refused with <c>PER_USER_LIMIT</c> — in both cases before the provider is ever
/// pushed to, and without touching another requester's own room under the per-user cap.
/// </summary>
public sealed class MusicServiceCapacityTests
{
    private static readonly Guid ChannelId = Guid.Parse("0192a000-0000-7000-8000-0000000ad001");

    private static string SearchJson(string id) =>
        """
            {"tracks":{"items":[{"name":"Song __ID__","uri":"spotify:track:__ID__","duration_ms":200000,"artists":[{"name":"Artist"}],"album":{"name":"Album","images":[]}}]}}
            """.Replace("__ID__", id);

    [Fact]
    public async Task A_queue_at_its_configured_cap_refuses_the_next_request_before_pushing_it()
    {
        (MusicService sut, RecordingHttpHandler handler) = Build(
            new MusicConfigDto(true, "auto", 2, 50, true, true, "everyone")
        );

        (await RequestAsync(sut, handler, "a1", "viewer1")).IsSuccess.Should().BeTrue();
        (await RequestAsync(sut, handler, "a2", "viewer2")).IsSuccess.Should().BeTrue();

        int pushesBeforeThird = QueuePushCount(handler);
        Result third = await RequestAsync(sut, handler, "a3", "viewer3");

        third.IsFailure.Should().BeTrue();
        third.ErrorCode.Should().Be("QUEUE_FULL");
        QueuePushCount(handler)
            .Should()
            .Be(pushesBeforeThird, "a refused request must never reach the provider");
        (await sut.GetQueueAsync(ChannelId.ToString())).Queue.Should().HaveCount(2);
    }

    [Fact]
    public async Task A_requester_at_their_per_user_cap_is_refused_while_another_requester_still_has_room()
    {
        (MusicService sut, RecordingHttpHandler handler) = Build(
            new MusicConfigDto(true, "auto", 50, 1, true, true, "everyone")
        );

        (await RequestAsync(sut, handler, "b1", "viewer1")).IsSuccess.Should().BeTrue();

        Result secondForSameViewer = await RequestAsync(sut, handler, "b2", "viewer1");
        secondForSameViewer.IsFailure.Should().BeTrue();
        secondForSameViewer.ErrorCode.Should().Be("PER_USER_LIMIT");

        // A different requester is nowhere near their own cap — the gate is per-owner, not a global lock.
        Result forAnotherViewer = await RequestAsync(sut, handler, "b3", "viewer2");
        forAnotherViewer.IsSuccess.Should().BeTrue();

        (await sut.GetQueueAsync(ChannelId.ToString()))
            .Queue.Select(q => q.TrackName)
            .Should()
            .Equal("Song b1", "Song b3");
    }

    private static int QueuePushCount(RecordingHttpHandler handler) =>
        handler.RequestUrls.Count(url =>
            url.Contains("/me/player/queue", StringComparison.Ordinal)
        );

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
        handler.RespondWhen(
            r =>
                r.Method == HttpMethod.Post
                && r.RequestUri!.AbsolutePath.EndsWith(
                    "/me/player/queue",
                    StringComparison.Ordinal
                ),
            HttpStatusCode.NoContent
        );
        return await sut.AddToQueueAsync(ChannelId.ToString(), $"song {trackId}", requestedBy);
    }

    private static (MusicService Sut, RecordingHttpHandler Handler) Build(MusicConfigDto config)
    {
        MusicTestDbContext db = MusicTestDbContext.New();
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
            NullSystemCredentialsProvider.Instance,
            new ConnectionRefreshGate(),
            new NullChannelCredentialsResolver(NullSystemCredentialsProvider.Instance)
        );

        IMusicConfigService configService = Substitute.For<IMusicConfigService>();
        configService
            .GetConfigAsync(ChannelId.ToString(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(config));

        MusicService sut = new(
            [spotify],
            db,
            new RecordingEventBus(),
            new BlockedTrackService(db),
            new SongRequestQueueStore(),
            new NoOpSongRequestQueuePersistence(),
            NullLogger<MusicService>.Instance,
            new InMemoryIntegrationCapabilityStore(),
            configService,
            Substitute.For<ICurrencyAccountService>(),
            new NowPlayingCache()
        );
        return (sut, handler);
    }
}
