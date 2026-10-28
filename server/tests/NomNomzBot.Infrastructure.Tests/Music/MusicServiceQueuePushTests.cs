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

using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Economy.Services;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Domain.Music.ValueObjects;
using NomNomzBot.Infrastructure.Identity;
using NomNomzBot.Infrastructure.Integrations;
using NomNomzBot.Infrastructure.Music;
using NomNomzBot.Infrastructure.Notifications;
using NomNomzBot.Infrastructure.Platform.Security;
using NomNomzBot.Infrastructure.Tests.Identity;
using NSubstitute;
using static NomNomzBot.Infrastructure.Tests.Music.PlaybackDirectorTestKit;

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
    public async Task Only_the_first_request_reaches_the_engine_the_rest_wait_in_the_fair_queue()
    {
        (MusicService sut, RecordingHttpHandler handler, DirectorRig rig) = Build();
        FakeEngine spotify = rig.Engines.EngineOf(ChannelId, EngineKind.Spotify);

        Result first = await RequestAsync(sut, handler, "a1", "viewer1");
        Result second = await RequestAsync(sut, handler, "a2", "viewer2");
        Result third = await RequestAsync(sut, handler, "a3", "viewer3");

        first.IsSuccess.Should().BeTrue();
        second.IsSuccess.Should().BeTrue();
        third.IsSuccess.Should().BeTrue();
        spotify
            .Calls.Where(c => c.Name == "Start")
            .Should()
            .HaveCount(1, "only the track now waiting to play belongs at the engine");
        rig.Registry.StateOf(ChannelId)
            .Item!.Uri.Should()
            .Be("spotify:track:a1", "the first request is the one the director started");
        QueuePushCount(handler).Should().Be(0, "nothing is pushed to the provider's own queue");
        // All three are accepted and ordered by US, and the order is the distinction that matters, not
        // the count: three separate viewers each get rank 1, so arrival order holds here.
        (await sut.GetQueueAsync(ChannelId.ToString()))
            .Queue.Select(i => i.TrackName)
            .Should()
            .Equal("Song a1", "Song a2", "Song a3");
    }

    [Fact]
    public async Task A_refused_start_takes_only_that_request_back_out_and_lets_the_next_one_through()
    {
        (MusicService sut, RecordingHttpHandler handler, DirectorRig rig) = Build();
        FakeEngine spotify = rig.Engines.EngineOf(ChannelId, EngineKind.Spotify);

        // Nothing is playing yet, so this request is the one the director starts, and the engine refuses.
        spotify.StartResult = EngineStartResult.BlockedBy(EngineBlockReason.NoOutput);
        Result failed = await RequestAsync(sut, handler, "b1", "viewer1");

        failed.ErrorCode.Should().Be("NO_ACTIVE_DEVICE");
        rig.Registry.StateOf(ChannelId).Kind.Should().Be(PlaybackStateKind.Blocked);
        (await sut.GetQueueAsync(ChannelId.ToString()))
            .Queue.Should()
            .BeEmpty("a request that never started is not left behind pretending to be live");

        // Nothing was left marked as in-flight either, so the next request is still started.
        spotify.StartResult = EngineStartResult.Ok;
        Result recovered = await RequestAsync(sut, handler, "b2", "viewer1");

        recovered.IsSuccess.Should().BeTrue();
        spotify.Calls.Where(c => c.Name == "Start").Should().HaveCount(2);
        rig.Registry.StateOf(ChannelId).Item!.Uri.Should().Be("spotify:track:b2");
        (await sut.GetQueueAsync(ChannelId.ToString()))
            .Queue.Select(i => i.TrackName)
            .Should()
            .Equal("Song b2");
    }

    /// <summary>
    /// A request the engine refused must not strand the queue or leave a phantom: it is rolled back, so
    /// nothing is left for the director to start, and once a request is started a further request never
    /// starts a second one while it plays. (The recovery tick that re-pushed a stuck head is gone: the
    /// director is the only thing that starts a request.)
    /// </summary>
    [Fact]
    public async Task A_refused_request_leaves_nothing_behind_and_a_started_one_is_not_started_twice()
    {
        (MusicService sut, RecordingHttpHandler handler, DirectorRig rig) = Build();
        FakeEngine spotify = rig.Engines.EngineOf(ChannelId, EngineKind.Spotify);
        spotify.StartResult = EngineStartResult.BlockedBy(EngineBlockReason.NoOutput);

        (await RequestAsync(sut, handler, "c1", "viewer1"))
            .ErrorCode.Should()
            .Be("NO_ACTIVE_DEVICE");
        (await RequestAsync(sut, handler, "c2", "viewer1"))
            .ErrorCode.Should()
            .Be("NO_ACTIVE_DEVICE");
        spotify.Calls.Where(c => c.Name == "Start").Should().HaveCount(2);

        // The device comes back. Both attempts rolled their request back out, so nothing is queued.
        spotify.StartResult = EngineStartResult.Ok;
        (await sut.GetQueueAsync(ChannelId.ToString()))
            .Queue.Should()
            .BeEmpty("both refused requests were rolled back out");
        QueuePushCount(handler).Should().Be(0);

        // A request accepted now is the one the director starts.
        (await RequestAsync(sut, handler, "c3", "viewer1"))
            .IsSuccess.Should()
            .BeTrue();
        spotify.Calls.Where(c => c.Name == "Start").Should().HaveCount(3);

        // A further request queues behind it; it never starts a second one while the first plays.
        (await RequestAsync(sut, handler, "c4", "viewer2"))
            .IsSuccess.Should()
            .BeTrue();

        spotify
            .Calls.Where(c => c.Name == "Start")
            .Should()
            .HaveCount(3, "one is already started, a later request must never start a second");
        rig.Registry.StateOf(ChannelId).Item!.Uri.Should().Be("spotify:track:c3");
        (await sut.GetQueueAsync(ChannelId.ToString()))
            .Queue.Select(i => i.TrackName)
            .Should()
            .Equal("Song c3", "Song c4");
        QueuePushCount(handler).Should().Be(0);
    }

    // POST only — a GET to the same path is the duplicate-check's own provider-queue probe (one per
    // admitted request, MusicService.CheckDuplicateAsync), not a push, and must not be counted as one.
    private static int QueuePushCount(RecordingHttpHandler handler) =>
        handler.RequestUrls.Count(url =>
            url.StartsWith("POST", StringComparison.Ordinal)
            && url.Contains("/me/player/queue", StringComparison.Ordinal)
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

    private static (MusicService Sut, RecordingHttpHandler Handler, DirectorRig Rig) Build()
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
            new SpotifyRateLimitCooldowns(new NullActionRequiredChangeNotifier()),
            new SingleHandlerClientFactory(handler),
            TimeProvider.System,
            NullLogger<SpotifyMusicProvider>.Instance,
            NullSystemCredentialsProvider.Instance,
            new ConnectionRefreshGate(),
            new NullChannelCredentialsResolver(NullSystemCredentialsProvider.Instance),
            new OutboundSanctionAccessor()
        );

        MusicService sut = new(
            [spotify],
            db,
            new RecordingEventBus(),
            new BlockedTrackService(db),
            DirectorRig.NewStore(out SongRequestQueueStore directorStore),
            new NoOpSongRequestQueuePersistence(),
            NullLogger<MusicService>.Instance,
            new InMemoryIntegrationCapabilityStore(),
            PermissiveMusicConfigService.Instance,
            Substitute.For<ICurrencyAccountService>(),
            new NowPlayingCache(),
            new OutboundSanctionAccessor(),
            Substitute.For<IUserIdentityService>(),
            Substitute.For<IForeignLinkTitleLookup>(),
            DirectorRig.Of(directorStore).Registry,
            DirectorRig.Of(directorStore).Registry
        );
        return (sut, handler, DirectorRig.Of(directorStore));
    }
}
