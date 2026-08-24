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
using NomNomzBot.Infrastructure.Identity;
using NomNomzBot.Infrastructure.Integrations;
using NomNomzBot.Infrastructure.Music;
using NomNomzBot.Infrastructure.Tests.Identity;

namespace NomNomzBot.Infrastructure.Tests.Music;

/// <summary>
/// S003 — a revoked/forbidden Spotify connection used to stay invisible: the integration kept looking
/// "connected" no matter what the provider actually said. Proves the fix at the served-data level (never
/// an internal flag): a live 401 flips <see cref="MusicService.GetActiveProviderAuthStatusAsync"/> — the
/// exact signal the Integrations card and the Music page both read — to <c>"needs_reauth"</c>; a live 403
/// for any reason OTHER than Spotify's own <c>PREMIUM_REQUIRED</c> flips it to <c>"forbidden"</c>,
/// distinctly; and a subsequent SUCCESSFUL call clears it back to healthy (null) — a stale broken status
/// that never clears would be its own lie. Also proves <c>!sr</c> (<see cref="MusicService.AddToQueueAsync"/>
/// / <c>RequestTrackAsync</c>) surfaces the same two reasons instead of a generic provider-error line,
/// reusing S002's typed-failure mechanism (<c>MusicAuthenticationFailedException</c> /
/// <see cref="NomNomzBot.Domain.Music.Exceptions.MusicForbiddenException"/>) rather than a parallel concept.
/// </summary>
public sealed class SpotifyMusicProviderAuthStatusTests
{
    private static readonly Guid ChannelId = Guid.Parse("0192a000-0000-7000-8000-0000000f6001");

    private const string NeedsReauthJson = """
        {"error":{"status":401,"reason":"UNAUTHORIZED","message":"The access token expired"}}
        """;

    private const string ForbiddenJson = """
        {"error":{"status":403,"reason":"FORBIDDEN","message":"You lack the required scope"}}
        """;

    private const string PremiumRequiredJson = """
        {"error":{"status":403,"reason":"PREMIUM_REQUIRED","message":"Premium required"}}
        """;

    [Fact]
    public async Task A_live_401_flips_the_served_auth_status_to_needs_reauth()
    {
        (MusicService sut, RecordingHttpHandler handler, _, _) = Build();
        handler.RespondWhen(
            r => r.RequestUri!.AbsolutePath.EndsWith("/me/player", StringComparison.Ordinal),
            HttpStatusCode.Unauthorized,
            NeedsReauthJson
        );

        // GetNowPlayingAsync is the exact call the Music page's now-playing read makes.
        await sut.GetNowPlayingAsync(ChannelId.ToString());

        string? status = await sut.GetActiveProviderAuthStatusAsync(ChannelId.ToString());
        status.Should().Be("needs_reauth");
    }

    [Fact]
    public async Task A_live_403_for_a_reason_other_than_premium_flips_the_served_auth_status_to_forbidden_distinctly()
    {
        (MusicService sut, RecordingHttpHandler handler, _, _) = Build();
        handler.RespondWhen(
            r => r.RequestUri!.AbsolutePath.EndsWith("/me/player", StringComparison.Ordinal),
            HttpStatusCode.Forbidden,
            ForbiddenJson
        );

        await sut.GetNowPlayingAsync(ChannelId.ToString());

        string? status = await sut.GetActiveProviderAuthStatusAsync(ChannelId.ToString());
        status.Should().Be("forbidden").And.NotBe("needs_reauth");
    }

    [Fact]
    public async Task A_403_whose_reason_IS_premium_required_never_flips_forbidden()
    {
        // PREMIUM_REQUIRED is its own long-standing signal (music-sr.md §3.5) — S003 must not conflate
        // "needs Premium" with "the grant is broken".
        (MusicService sut, RecordingHttpHandler handler, _, _) = Build();
        handler.RespondWhen(
            r => r.RequestUri!.AbsolutePath.EndsWith("/me/player/pause", StringComparison.Ordinal),
            HttpStatusCode.Forbidden,
            PremiumRequiredJson
        );

        Result played = await sut.PauseAsync(ChannelId.ToString());

        played.ErrorCode.Should().Be("PREMIUM_REQUIRED");
        string? status = await sut.GetActiveProviderAuthStatusAsync(ChannelId.ToString());
        status.Should().BeNull("a Premium rejection is not an auth-broken connection");
    }

    [Fact]
    public async Task A_later_successful_call_clears_needs_reauth_back_to_healthy()
    {
        (MusicService sut, RecordingHttpHandler handler, _, _) = Build();
        handler.RespondWhen(
            r => r.RequestUri!.AbsolutePath.EndsWith("/me/player", StringComparison.Ordinal),
            HttpStatusCode.Unauthorized,
            NeedsReauthJson
        );
        await sut.GetNowPlayingAsync(ChannelId.ToString());
        (await sut.GetActiveProviderAuthStatusAsync(ChannelId.ToString()))
            .Should()
            .Be("needs_reauth");

        // The connection recovers (streamer re-authorized) — the next call succeeds.
        handler.ClearRoutes();
        handler.RespondWhen(
            r => r.RequestUri!.AbsolutePath.EndsWith("/me/player", StringComparison.Ordinal),
            HttpStatusCode.NoContent
        );
        await sut.GetNowPlayingAsync(ChannelId.ToString());

        string? status = await sut.GetActiveProviderAuthStatusAsync(ChannelId.ToString());
        status.Should().BeNull("a stale needs_reauth that never clears would be its own lie");
    }

    [Fact]
    public async Task A_later_successful_call_clears_forbidden_back_to_healthy()
    {
        (MusicService sut, RecordingHttpHandler handler, _, _) = Build();
        handler.RespondWhen(
            r => r.RequestUri!.AbsolutePath.EndsWith("/me/player", StringComparison.Ordinal),
            HttpStatusCode.Forbidden,
            ForbiddenJson
        );
        await sut.GetNowPlayingAsync(ChannelId.ToString());
        (await sut.GetActiveProviderAuthStatusAsync(ChannelId.ToString())).Should().Be("forbidden");

        handler.ClearRoutes();
        handler.RespondWhen(
            r => r.RequestUri!.AbsolutePath.EndsWith("/me/player", StringComparison.Ordinal),
            HttpStatusCode.NoContent
        );
        await sut.GetNowPlayingAsync(ChannelId.ToString());

        (await sut.GetActiveProviderAuthStatusAsync(ChannelId.ToString())).Should().BeNull();
    }

    [Fact]
    public async Task Sr_queue_push_rejected_401_fails_as_MUSIC_AUTH_FAILED_not_a_generic_PROVIDER_ERROR()
    {
        (MusicService sut, RecordingHttpHandler handler, _, _) = Build();
        handler.RespondWhen(
            r =>
                r.Method == HttpMethod.Post
                && r.RequestUri!.AbsolutePath.EndsWith(
                    "/me/player/queue",
                    StringComparison.Ordinal
                ),
            HttpStatusCode.Unauthorized,
            NeedsReauthJson
        );

        Result result = await sut.AddToQueueAsync(ChannelId.ToString(), "spotify:track:live401");

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("MUSIC_AUTH_FAILED");
        result.ErrorMessage.Should().Contain("reconnected");
    }

    [Fact]
    public async Task Sr_queue_push_rejected_403_non_premium_fails_as_MUSIC_FORBIDDEN_distinct_from_auth_failed()
    {
        (MusicService sut, RecordingHttpHandler handler, _, _) = Build();
        handler.RespondWhen(
            r =>
                r.Method == HttpMethod.Post
                && r.RequestUri!.AbsolutePath.EndsWith(
                    "/me/player/queue",
                    StringComparison.Ordinal
                ),
            HttpStatusCode.Forbidden,
            ForbiddenJson
        );

        Result result = await sut.AddToQueueAsync(ChannelId.ToString(), "spotify:track:live403");

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("MUSIC_FORBIDDEN");
        result.ErrorMessage.Should().Contain("permission");
    }

    // ─── Harness ──────────────────────────────────────────────────────────────

    private static (
        MusicService Sut,
        RecordingHttpHandler Handler,
        InMemoryIntegrationCapabilityStore CapabilityStore,
        FakeIntegrationTokenVault Vault
    ) Build()
    {
        MusicTestDbContext db = new(
            new DbContextOptionsBuilder<MusicTestDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options
        );
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
        vault.SeedConnectedSpotify(ChannelId);

        RecordingHttpHandler handler = new();
        InMemoryIntegrationCapabilityStore capabilityStore = new();
        SpotifyMusicProvider spotify = new(
            db,
            vault,
            capabilityStore,
            new LastActiveSpotifyDeviceTracker(),
            new SingleHandlerClientFactory(handler),
            TimeProvider.System,
            NullLogger<SpotifyMusicProvider>.Instance,
            NullSystemCredentialsProvider.Instance,
            new ConnectionRefreshGate()
        );

        MusicService sut = new(
            [spotify],
            db,
            new RecordingEventBus(),
            new BlockedTrackService(db),
            new SongRequestQueueStore(),
            new NoOpSongRequestQueuePersistence(),
            NullLogger<MusicService>.Instance,
            capabilityStore
        );

        return (sut, handler, capabilityStore, vault);
    }
}
