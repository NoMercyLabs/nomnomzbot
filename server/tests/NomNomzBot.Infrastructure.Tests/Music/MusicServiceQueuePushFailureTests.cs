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
using NomNomzBot.Application.Music.Services;
using NomNomzBot.Infrastructure.Identity;
using NomNomzBot.Infrastructure.Integrations;
using NomNomzBot.Infrastructure.Music;
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
    // POST only — a GET to the same path is the duplicate-check's own provider-queue probe (one per
    // admitted request, MusicService.CheckDuplicateAsync), not a push, and must not be counted as one.
    private static int QueuePushCount(RecordingHttpHandler handler) =>
        handler.RequestUrls.Count(url =>
            url.StartsWith("POST", StringComparison.Ordinal)
            && url.Contains("/me/player/queue", StringComparison.Ordinal)
        );

    private static bool IsQueuePush(HttpRequestMessage r) =>
        r.Method == HttpMethod.Post
        && r.RequestUri!.AbsolutePath.EndsWith("/me/player/queue", StringComparison.Ordinal);

    private static void RespondWithResolvedTrack(RecordingHttpHandler handler) =>
        handler.RespondWhen(
            r =>
                r.RequestUri!.AbsolutePath.EndsWith($"/tracks/{TrackId}", StringComparison.Ordinal),
            HttpStatusCode.OK,
            TrackJson
        );

    [Fact]
    public async Task No_active_device_on_the_initial_push_fails_the_request_with_its_own_code_and_reply()
    {
        (MusicService sut, RecordingHttpHandler handler, _, _, _) = Build();
        RespondWithResolvedTrack(handler);
        handler.RespondWhen(IsQueuePush, HttpStatusCode.NotFound, NoActiveDeviceJson);

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
            .BeEmpty("a push that never reached the provider must not leave a phantom queue entry");
    }

    [Fact]
    public async Task Premium_required_on_the_initial_push_fails_the_request_with_its_own_code_and_reply()
    {
        (MusicService sut, RecordingHttpHandler handler, _, _, _) = Build();
        RespondWithResolvedTrack(handler);
        handler.RespondWhen(IsQueuePush, HttpStatusCode.Forbidden, PremiumRequiredJson);

        Result<MusicTrack> result = await sut.RequestTrackAsync(
            ChannelId.ToString(),
            $"https://open.spotify.com/track/{TrackId}",
            "viewer1"
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("PREMIUM_REQUIRED");
        result.ErrorMessage.Should().Contain("Premium");
        (await sut.GetQueueAsync(ChannelId.ToString())).Queue.Should().BeEmpty();
    }

    [Fact]
    public async Task Dead_connection_on_the_initial_push_fails_as_MUSIC_AUTH_FAILED()
    {
        (
            MusicService sut,
            RecordingHttpHandler handler,
            _,
            FakeIntegrationTokenVault vault,
            Guid connectionId
        ) = Build();
        // Expired token, no refresh token on file — GetTokenAsync resolves to null for every Spotify
        // call (resolve/search included), so AddToQueueAsync (not RequestTrackAsync) is the entry point
        // here: it degrades an unresolvable track to a synthetic display entry and still attempts the
        // real admission — including the immediate provider push, which is what must surface the auth
        // failure rather than a misleading NOT_FOUND from the resolve step.
        vault.MakeUnrefreshable(connectionId);

        Result result = await sut.AddToQueueAsync(ChannelId.ToString(), TrackUri, "viewer1");

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("MUSIC_AUTH_FAILED");
        result.ErrorMessage.Should().Contain("reconnected");
        handler
            .RequestUrls.Should()
            .NotContain(u => u.Contains("/me/player/queue", StringComparison.Ordinal));
        (await sut.GetQueueAsync(ChannelId.ToString())).Queue.Should().BeEmpty();
    }

    [Fact]
    public async Task An_unrecognised_provider_failure_on_the_initial_push_falls_back_to_PROVIDER_ERROR()
    {
        (MusicService sut, RecordingHttpHandler handler, _, _, _) = Build();
        RespondWithResolvedTrack(handler);
        handler.RespondWhen(IsQueuePush, HttpStatusCode.InternalServerError, "{}");

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
    public async Task A_successful_initial_push_still_queues_and_replies_with_the_track()
    {
        (MusicService sut, RecordingHttpHandler handler, _, _, _) = Build();
        RespondWithResolvedTrack(handler);
        handler.RespondWhen(IsQueuePush, HttpStatusCode.NoContent);

        Result<MusicTrack> result = await sut.RequestTrackAsync(
            ChannelId.ToString(),
            $"https://open.spotify.com/track/{TrackId}",
            "viewer1"
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.Name.Should().Be("Never Gonna Give You Up");
        (await sut.GetQueueAsync(ChannelId.ToString())).Queue.Should().ContainSingle();
    }

    [Fact]
    public async Task Skip_does_not_push_the_next_track_again_because_the_provider_already_holds_it()
    {
        (MusicService sut, RecordingHttpHandler handler, _, _, _) = Build();
        RespondWithResolvedTrack(handler);
        handler.RespondWhen(IsQueuePush, HttpStatusCode.NoContent);

        // Admission pushes the request to the provider's own queue — that is what plays it.
        await sut.RequestTrackAsync(
            ChannelId.ToString(),
            $"https://open.spotify.com/track/{TrackId}",
            "viewer1"
        );
        int pushesAfterRequest = QueuePushCount(handler);
        pushesAfterRequest.Should().Be(1);

        Result skip = await sut.SkipAsync(ChannelId.ToString());

        skip.IsSuccess.Should().BeTrue();
        // A skip only advances the provider. Re-pushing the entry here would queue the same track a
        // second time, which is exactly the double-play the old dequeue-and-push skip produced.
        QueuePushCount(handler).Should().Be(pushesAfterRequest);
        // The entry stays pending until the live playback state confirms it is the track now playing.
        (await sut.GetQueueAsync(ChannelId.ToString()))
            .Queue.Should()
            .ContainSingle();
    }

    [Fact]
    public async Task Skip_still_succeeds_when_the_provider_push_succeeds()
    {
        (MusicService sut, RecordingHttpHandler handler, _, _, _) = Build();
        RespondWithResolvedTrack(handler);
        handler.RespondWhen(IsQueuePush, HttpStatusCode.NoContent);
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

        Result skip = await sut.SkipAsync(ChannelId.ToString());

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
        Guid ConnectionId
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
            new SingleHandlerClientFactory(handler),
            TimeProvider.System,
            NullLogger<SpotifyMusicProvider>.Instance,
            NullSystemCredentialsProvider.Instance,
            new ConnectionRefreshGate(),
            new NullChannelCredentialsResolver(NullSystemCredentialsProvider.Instance)
        );

        RecordingEventBus bus = new();
        BlockedTrackService blocks = new(db);
        MusicService sut = new(
            [spotify],
            db,
            bus,
            blocks,
            new SongRequestQueueStore(),
            new NoOpSongRequestQueuePersistence(),
            NullLogger<MusicService>.Instance,
            new InMemoryIntegrationCapabilityStore(),
            PermissiveMusicConfigService.Instance,
            Substitute.For<ICurrencyAccountService>(),
            new NowPlayingCache()
        );
        return (sut, handler, db, vault, connectionId);
    }
}
