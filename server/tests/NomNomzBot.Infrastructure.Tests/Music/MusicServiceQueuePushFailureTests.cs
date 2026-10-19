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
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Application.Music.Services;
using NomNomzBot.Infrastructure.Identity;
using NomNomzBot.Infrastructure.Integrations;
using NomNomzBot.Infrastructure.Music;
using NomNomzBot.Infrastructure.Notifications;
using NomNomzBot.Infrastructure.Platform.Security;
using NomNomzBot.Infrastructure.Tests.Identity;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Music;

/// <summary>
/// Proves S002: the provider's queue-push outcome is honoured instead of ignored. Before this slice,
/// <see cref="Domain.Music.Interfaces.IMusicProvider.AddToQueueAsync"/>'s return value was discarded —
/// a failed push (no active device, dead token, provider 5xx) still reported "queued" success to the
/// viewer, and (for the initial provider push) still left the request looking live in the fair queue.
/// Each distinct failure class now maps to its own <c>Result</c> error code and its own viewer-facing
/// chat line, and the queue store never carries a phantom "queued" entry that never actually reached
/// the provider.
/// </summary>
public sealed class MusicServiceQueuePushFailureTests
{
    private static readonly Guid ChannelId = Guid.Parse("0192a000-0000-7000-8000-000000007801");
    private const string TrackId = "4uLU6hMCjMI75M1A2tKUQC";
    private const string TrackUri = $"spotify:track:{TrackId}";

    private const string TrackJson = """
        {"id":"4uLU6hMCjMI75M1A2tKUQC","name":"Never Gonna Give You Up",
         "uri":"spotify:track:4uLU6hMCjMI75M1A2tKUQC","duration_ms":213573,
         "artists":[{"name":"Rick Astley"}],
         "album":{"name":"Whenever You Need Somebody","images":[]}}
        """;

    private const string NoActiveDeviceJson = """
        {"error":{"status":404,"reason":"NO_ACTIVE_DEVICE","message":"No active device found"}}
        """;

    private const string PremiumRequiredJson = """
        {"error":{"status":403,"reason":"PREMIUM_REQUIRED","message":"Premium required"}}
        """;

    // POST only — a GET to the same path is the duplicate-check's own provider-queue probe (one per
    // admitted request, MusicService.CheckDuplicateAsync), not a push, and must not be counted as one.
    private static int QueuePushCount(RecordingHttpHandler handler) =>
        handler.RequestUrls.Count(url =>
            url.StartsWith("POST", StringComparison.Ordinal)
            && url.Contains("/me/player/queue", StringComparison.Ordinal)
        );

    /// <summary>The call the Spotify engine makes to start a track: a PUT to the play endpoint.</summary>
    private static bool IsStartTrack(HttpRequestMessage r) =>
        r.Method == HttpMethod.Put
        && r.RequestUri!.AbsolutePath.EndsWith("/me/player/play", StringComparison.Ordinal);

    private static int StartTrackCount(RecordingHttpHandler handler) =>
        handler.RequestUrls.Count(url =>
            url.StartsWith("PUT", StringComparison.Ordinal)
            && url.Contains("/me/player/play", StringComparison.Ordinal)
        );

    private static void RespondWithResolvedTrack(RecordingHttpHandler handler) =>
        handler.RespondWhen(
            r =>
                r.RequestUri!.AbsolutePath.EndsWith($"/tracks/{TrackId}", StringComparison.Ordinal),
            HttpStatusCode.OK,
            TrackJson
        );

    [Fact]
    public async Task No_active_device_on_the_start_fails_the_request_with_its_own_code_and_reply()
    {
        (MusicService sut, RecordingHttpHandler handler, _, _, _, _) = Build();
        RespondWithResolvedTrack(handler);
        handler.RespondWhen(IsStartTrack, HttpStatusCode.NotFound, NoActiveDeviceJson);

        Result<MusicTrack> result = await sut.RequestTrackAsync(
            ChannelId.ToString(),
            $"https://open.spotify.com/track/{TrackId}",
            "viewer1"
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("NO_ACTIVE_DEVICE");
        result
            .ErrorMessage.Should()
            .Contain("Never Gonna Give You Up")
            .And.Contain("Start playback");
        (await sut.GetQueueAsync(ChannelId.ToString()))
            .Queue.Should()
            .BeEmpty("a start that never reached the player must not leave a phantom queue entry");
        QueuePushCount(handler)
            .Should()
            .Be(0, "the director starts the track, nothing is queued at the provider");
    }

    [Fact]
    public async Task Premium_required_on_the_start_fails_the_request_as_MUSIC_FORBIDDEN()
    {
        (MusicService sut, RecordingHttpHandler handler, _, _, _, _) = Build();
        RespondWithResolvedTrack(handler);
        handler.RespondWhen(IsStartTrack, HttpStatusCode.Forbidden, PremiumRequiredJson);

        Result<MusicTrack> result = await sut.RequestTrackAsync(
            ChannelId.ToString(),
            $"https://open.spotify.com/track/{TrackId}",
            "viewer1"
        );

        result.IsFailure.Should().BeTrue();
        // The engine folds a Premium refusal into Forbidden, so the requester gets the forbidden reply.
        result.ErrorCode.Should().Be("MUSIC_FORBIDDEN");
        result.ErrorMessage.Should().Contain("permission");
        (await sut.GetQueueAsync(ChannelId.ToString())).Queue.Should().BeEmpty();
    }

    [Fact]
    public async Task Dead_connection_on_the_start_fails_as_MUSIC_AUTH_FAILED()
    {
        (
            MusicService sut,
            RecordingHttpHandler handler,
            _,
            FakeIntegrationTokenVault vault,
            Guid connectionId,
            _
        ) = Build();
        // Expired token, no refresh token on file — GetTokenAsync resolves to null for every Spotify
        // call (resolve/search included), so AddToQueueAsync (not RequestTrackAsync) is the entry point
        // here: it degrades an unresolvable track to a synthetic display entry and still attempts the
        // real admission, including the director's start, which is what must surface the auth
        // failure rather than a misleading NOT_FOUND from the resolve step.
        vault.MakeUnrefreshable(connectionId);

        Result result = await sut.AddToQueueAsync(ChannelId.ToString(), TrackUri, "viewer1");

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("MUSIC_AUTH_FAILED");
        result.ErrorMessage.Should().Contain("reconnected");
        handler
            .RequestUrls.Should()
            .NotContain(
                u => u.Contains("/me/player/", StringComparison.Ordinal),
                "the engine never sends a command without a token"
            );
        (await sut.GetQueueAsync(ChannelId.ToString())).Queue.Should().BeEmpty();
    }

    [Fact]
    public async Task An_unrecognised_provider_failure_on_the_start_falls_back_to_PROVIDER_ERROR()
    {
        (MusicService sut, RecordingHttpHandler handler, _, _, _, _) = Build();
        RespondWithResolvedTrack(handler);
        handler.RespondWhen(IsStartTrack, HttpStatusCode.InternalServerError, "{}");

        Result<MusicTrack> result = await sut.RequestTrackAsync(
            ChannelId.ToString(),
            $"https://open.spotify.com/track/{TrackId}",
            "viewer1"
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("PROVIDER_ERROR");
        (await sut.GetQueueAsync(ChannelId.ToString())).Queue.Should().BeEmpty();
    }

    [Fact]
    public async Task A_successful_start_still_queues_and_replies_with_the_track()
    {
        (MusicService sut, RecordingHttpHandler handler, _, _, _, _) = Build();
        RespondWithResolvedTrack(handler);
        handler.RespondWhen(IsStartTrack, HttpStatusCode.NoContent);

        Result<MusicTrack> result = await sut.RequestTrackAsync(
            ChannelId.ToString(),
            $"https://open.spotify.com/track/{TrackId}",
            "viewer1"
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.Name.Should().Be("Never Gonna Give You Up");
        StartTrackCount(handler).Should().Be(1);
        (await sut.GetQueueAsync(ChannelId.ToString())).Queue.Should().ContainSingle();
    }

    [Fact]
    public async Task Skip_does_not_start_the_next_track_again_because_the_director_already_started_it()
    {
        (MusicService sut, RecordingHttpHandler handler, _, _, _, _) = Build();
        RespondWithResolvedTrack(handler);
        handler.RespondWhen(IsStartTrack, HttpStatusCode.NoContent);

        // Admission sends the request to the director, whose Spotify engine starts it, and that is what plays it.
        await sut.RequestTrackAsync(
            ChannelId.ToString(),
            $"https://open.spotify.com/track/{TrackId}",
            "viewer1"
        );
        int startsAfterRequest = StartTrackCount(handler);
        startsAfterRequest.Should().Be(1);
        QueuePushCount(handler).Should().Be(0);

        Result skip = await sut.SkipAsync(ChannelId.ToString(), "4242");

        skip.IsSuccess.Should().BeTrue();
        // A skip only advances the provider. Starting the entry again here would play the same track a
        // second time, which is exactly the double-play the old dequeue-and-push skip produced.
        StartTrackCount(handler).Should().Be(startsAfterRequest);
        QueuePushCount(handler).Should().Be(0);
        // The entry stays pending until the live playback state confirms it is the track now playing.
        (await sut.GetQueueAsync(ChannelId.ToString()))
            .Queue.Should()
            .ContainSingle();
    }

    [Fact]
    public async Task Skip_still_succeeds_when_the_provider_push_succeeds()
    {
        (MusicService sut, RecordingHttpHandler handler, _, _, _, _) = Build();
        RespondWithResolvedTrack(handler);
        handler.RespondWhen(IsStartTrack, HttpStatusCode.NoContent);
        handler.RespondWhen(
            r =>
                r.Method == HttpMethod.Post
                && r.RequestUri!.AbsolutePath.EndsWith("/me/player/next", StringComparison.Ordinal),
            HttpStatusCode.NoContent
        );

        await sut.RequestTrackAsync(
            ChannelId.ToString(),
            $"https://open.spotify.com/track/{TrackId}",
            "viewer1"
        );
        await sut.AddToQueueAsync(ChannelId.ToString(), TrackUri, "viewer2");

        Result skip = await sut.SkipAsync(ChannelId.ToString(), "4242");

        skip.IsSuccess.Should().BeTrue();
        (await sut.GetQueueAsync(ChannelId.ToString()))
            .Queue.Should()
            .ContainSingle("the dequeued track was pushed successfully and is not put back");
    }

    // ─── Harness ──────────────────────────────────────────────────────────────

    private static (
        MusicService Sut,
        RecordingHttpHandler Handler,
        MusicTestDbContext Db,
        FakeIntegrationTokenVault Vault,
        Guid ConnectionId,
        DirectorRig Rig
    ) Build()
    {
        MusicTestDbContext db = MusicTestDbContext.New();
        // Routing seed: MusicService.GetActiveProviderAsync selects the active provider by which
        // Service names are connected — a separate concern from SpotifyMusicProvider's OWN token
        // resolution (which reads the vault below, S003).
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
        Guid connectionId = vault.SeedConnectedSpotify(ChannelId);

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

        RecordingEventBus bus = new();
        BlockedTrackService blocks = new(db);
        DirectorRig.NewStore(out SongRequestQueueStore directorStore);
        // The real Spotify engine, over this provider: a refused start comes from the provider's own HTTP answer.
        DirectorRig.Of(directorStore).Engines.UseRealSpotify(ChannelId, spotify);
        MusicService sut = new(
            [spotify],
            db,
            bus,
            blocks,
            directorStore,
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
        return (sut, handler, db, vault, connectionId, DirectorRig.Of(directorStore));
    }
}
