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
using NomNomzBot.Domain.Music.Events;
using NomNomzBot.Domain.Platform.Entities;
using NomNomzBot.Infrastructure.Integrations;
using NomNomzBot.Infrastructure.Music;
using NomNomzBot.Infrastructure.Tests.Identity;

namespace NomNomzBot.Infrastructure.Tests.Music;

/// <summary>
/// Proves the Spotify Premium enforcement (music-sr.md §3.5): a player write rejected with
/// Spotify's real 403/<c>PREMIUM_REQUIRED</c> error envelope surfaces as
/// <c>Failure("PREMIUM_REQUIRED")</c> — distinct from <c>CAPABILITY_UNSUPPORTED</c>, never a throw
/// at the consumer — on multiple transport members, records the observed
/// <c>spotify.premium=false</c> capability for the integrations status surface (and flips it back
/// to true on a successful player write), and never publishes a playback-state change for the
/// failed command. A plain 403 without the premium reason is NOT treated as a premium failure.
/// </summary>
public sealed class SpotifyMusicProviderTransportTests
{
    private static readonly Guid ChannelId = Guid.Parse("0192a000-0000-7000-8000-0000000f5001");

    private const string PremiumRequiredJson = """
        {"error":{"status":403,"reason":"PREMIUM_REQUIRED","message":"Player command failed: Premium required"}}
        """;

    [Fact]
    public async Task Premium_403_on_volume_maps_to_PREMIUM_REQUIRED_not_a_throw()
    {
        (MusicService sut, _, RecordingHttpHandler handler, _) = Build();
        handler.RespondWhen(
            r => r.RequestUri!.AbsolutePath.EndsWith("/me/player/volume", StringComparison.Ordinal),
            HttpStatusCode.Forbidden,
            PremiumRequiredJson
        );

        Func<Task<Result>> act = () => sut.SetVolumeAsync(ChannelId.ToString(), 40);

        Result result = (await act.Should().NotThrowAsync()).Subject;
        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("PREMIUM_REQUIRED");
    }

    [Fact]
    public async Task Premium_403_on_pause_maps_to_PREMIUM_REQUIRED_and_publishes_nothing()
    {
        (MusicService sut, RecordingEventBus bus, RecordingHttpHandler handler, _) = Build();
        handler.RespondWhen(
            r => r.RequestUri!.AbsolutePath.EndsWith("/me/player/pause", StringComparison.Ordinal),
            HttpStatusCode.Forbidden,
            PremiumRequiredJson
        );

        Result result = await sut.PauseAsync(ChannelId.ToString());

        result.ErrorCode.Should().Be("PREMIUM_REQUIRED");
        bus.Published.OfType<PlaybackStateChangedEvent>()
            .Should()
            .BeEmpty("a rejected command must not broadcast a state change");
    }

    [Fact]
    public async Task Premium_403_on_previous_maps_to_PREMIUM_REQUIRED()
    {
        (MusicService sut, _, RecordingHttpHandler handler, _) = Build();
        handler.RespondWhen(
            r =>
                r.RequestUri!.AbsolutePath.EndsWith(
                    "/me/player/previous",
                    StringComparison.Ordinal
                ),
            HttpStatusCode.Forbidden,
            PremiumRequiredJson
        );

        Result result = await sut.PreviousAsync(ChannelId.ToString());

        result.ErrorCode.Should().Be("PREMIUM_REQUIRED");
    }

    [Fact]
    public async Task Premium_detection_flips_the_observed_spotify_premium_capability()
    {
        (
            MusicService sut,
            _,
            RecordingHttpHandler handler,
            InMemoryIntegrationCapabilityStore store
        ) = Build();
        handler.RespondWhen(
            r => r.RequestUri!.AbsolutePath.EndsWith("/me/player/volume", StringComparison.Ordinal),
            HttpStatusCode.Forbidden,
            PremiumRequiredJson
        );

        await sut.SetVolumeAsync(ChannelId.ToString(), 40);

        store
            .GetObserved(ChannelId, "spotify")
            .Should()
            .Contain(new KeyValuePair<string, bool>("spotify.premium", false));
    }

    [Fact]
    public async Task A_successful_player_write_records_spotify_premium_true()
    {
        (
            MusicService sut,
            _,
            RecordingHttpHandler handler,
            InMemoryIntegrationCapabilityStore store
        ) = Build();
        handler.RespondWhen(
            r => r.RequestUri!.AbsolutePath.EndsWith("/me/player/volume", StringComparison.Ordinal),
            HttpStatusCode.NoContent
        );

        Result result = await sut.SetVolumeAsync(ChannelId.ToString(), 40);

        result.IsSuccess.Should().BeTrue();
        store
            .GetObserved(ChannelId, "spotify")
            .Should()
            .Contain(new KeyValuePair<string, bool>("spotify.premium", true));
    }

    [Fact]
    public async Task A_403_without_the_premium_reason_is_not_a_premium_failure()
    {
        (
            MusicService sut,
            _,
            RecordingHttpHandler handler,
            InMemoryIntegrationCapabilityStore store
        ) = Build();
        handler.RespondWhen(
            r => r.RequestUri!.AbsolutePath.EndsWith("/me/player/volume", StringComparison.Ordinal),
            HttpStatusCode.Forbidden,
            """{"error":{"status":403,"reason":"UNKNOWN","message":"Restriction violated"}}"""
        );

        Result result = await sut.SetVolumeAsync(ChannelId.ToString(), 40);

        result.ErrorCode.Should().NotBe("PREMIUM_REQUIRED");
        store
            .GetObserved(ChannelId, "spotify")
            .Should()
            .NotContainKey("spotify.premium", "an unrelated 403 proves nothing about Premium");
    }

    // ─── Harness ──────────────────────────────────────────────────────────────

    private static (
        MusicService Sut,
        RecordingEventBus Bus,
        RecordingHttpHandler Handler,
        InMemoryIntegrationCapabilityStore Store
    ) Build()
    {
        MusicTestDbContext db = new(
            new DbContextOptionsBuilder<MusicTestDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options
        );
        db.Services.Add(
            new Service
            {
                Id = Guid.NewGuid().ToString(),
                Name = "spotify",
                BroadcasterId = ChannelId,
                Enabled = true,
                AccessToken = "test-access-token",
            }
        );
        db.SaveChanges();

        RecordingHttpHandler handler = new();
        InMemoryIntegrationCapabilityStore store = new();
        SpotifyMusicProvider spotify = new(
            db,
            new PassthroughProtector(),
            store,
            new SingleHandlerClientFactory(handler),
            TimeProvider.System,
            NullLogger<SpotifyMusicProvider>.Instance
        );

        RecordingEventBus bus = new();
        MusicService sut = new(
            [spotify],
            db,
            bus,
            new BlockedTrackService(db),
            NullLogger<MusicService>.Instance
        );
        return (sut, bus, handler, store);
    }
}
