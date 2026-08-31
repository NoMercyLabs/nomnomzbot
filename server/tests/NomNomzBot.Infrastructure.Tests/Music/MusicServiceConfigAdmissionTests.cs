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
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Infrastructure.Identity;
using NomNomzBot.Infrastructure.Integrations;
using NomNomzBot.Infrastructure.Music;
using NomNomzBot.Infrastructure.Tests.Identity;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Music;

/// <summary>
/// Proves the <c>MusicConfig</c> admission gate inside <see cref="MusicService.RequestTrackAsync"/> (S067):
/// a channel with <c>IsEnabled=false</c> refuses every request with <c>SR_DISABLED</c> before it ever
/// resolves a provider; a requester below the configured <c>MinTrustLevel</c> floor is refused with
/// <c>MIN_TRUST_LEVEL</c>; a requester at or above the floor is admitted; and a caller that passes no role
/// level at all (dashboard/public-page/script — surfaces with their own authorization boundary) skips the
/// trust-level check but is still stopped by <c>IsEnabled</c>. Neither gate ever reaches the provider or the
/// fair queue when it refuses — proven the same way <see cref="MusicServiceBlockedAdmissionTests"/> proves
/// the blocklist gate: no queue-changed event, an empty queue.
/// </summary>
public sealed class MusicServiceConfigAdmissionTests
{
    private static readonly Guid ChannelId = Guid.Parse("0192a000-0000-7000-8000-0000000ac001");

    [Fact]
    public async Task IsEnabled_false_refuses_every_request_before_resolving_a_provider()
    {
        (MusicService sut, RecordingHttpHandler handler) = Build(
            new MusicConfigDto(false, "auto", 50, 5, true, true, "everyone")
        );

        Result<MusicTrack> result = await sut.RequestTrackAsync(
            ChannelId.ToString(),
            "never gonna give you up"
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("SR_DISABLED");
        // No provider call at all — the gate refused before GetActiveProviderAsync ever ran.
        handler.RequestUrls.Should().BeEmpty();
        (await sut.GetQueueAsync(ChannelId.ToString())).Queue.Should().BeEmpty();
    }

    [Fact]
    public async Task A_requester_below_the_min_trust_level_floor_is_refused()
    {
        (MusicService sut, _) = Build(
            new MusicConfigDto(true, "auto", 50, 5, true, true, "moderators")
        );

        Result<MusicTrack> result = await sut.RequestTrackAsync(
            ChannelId.ToString(),
            "never gonna give you up",
            requestedBy: "viewer1",
            requesterRoleLevel: PermissionLevel.Subscriber.ToLevelValue()
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("MIN_TRUST_LEVEL");
        (await sut.GetQueueAsync(ChannelId.ToString())).Queue.Should().BeEmpty();
    }

    [Fact]
    public async Task A_requester_at_the_min_trust_level_floor_is_admitted()
    {
        (MusicService sut, RecordingHttpHandler handler) = Build(
            new MusicConfigDto(true, "auto", 50, 5, true, true, "moderators")
        );
        RouteTrackResolve(handler);

        Result<MusicTrack> result = await sut.RequestTrackAsync(
            ChannelId.ToString(),
            "spotify:track:q1",
            requestedBy: "mod1",
            requesterRoleLevel: PermissionLevel.Moderator.ToLevelValue()
        );

        result.IsSuccess.Should().BeTrue();
        (await sut.GetQueueAsync(ChannelId.ToString())).Queue.Should().ContainSingle();
    }

    [Fact]
    public async Task ANullRoleLevel_skips_the_trust_floor_but_isEnabled_still_applies()
    {
        // Dashboard / public-page / script callers pass no role level (their own authorization already
        // ran) — a strict floor must not block them, but the channel being off must still refuse them.
        (MusicService sutOff, _) = Build(
            new MusicConfigDto(false, "auto", 50, 5, true, true, "broadcaster")
        );
        Result<MusicTrack> refused = await sutOff.RequestTrackAsync(
            ChannelId.ToString(),
            "never gonna give you up"
        );
        refused.ErrorCode.Should().Be("SR_DISABLED");

        (MusicService sutOn, RecordingHttpHandler handler) = Build(
            new MusicConfigDto(true, "auto", 50, 5, true, true, "broadcaster")
        );
        RouteTrackResolve(handler);
        Result<MusicTrack> admitted = await sutOn.RequestTrackAsync(
            ChannelId.ToString(),
            "spotify:track:q1"
        );
        admitted.IsSuccess.Should().BeTrue();
    }

    // ─── Harness ──────────────────────────────────────────────────────────────

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
            Substitute.For<ICurrencyAccountService>()
        );
        return (sut, handler);
    }

    private static void RouteTrackResolve(RecordingHttpHandler handler)
    {
        const string searchJson = """
            {"tracks":{"items":[{"name":"Song Q","uri":"spotify:track:q1","duration_ms":200000,"artists":[{"name":"Artist"}],"album":{"name":"Album","images":[]}}]}}
            """;
        handler.RespondWhen(
            r => r.RequestUri!.AbsolutePath.EndsWith("/search", StringComparison.Ordinal),
            HttpStatusCode.OK,
            searchJson
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
    }
}
