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
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Domain.Music.Interfaces;
using NomNomzBot.Infrastructure.Identity;
using NomNomzBot.Infrastructure.Integrations;
using NomNomzBot.Infrastructure.Music;
using NomNomzBot.Infrastructure.Notifications;
using NomNomzBot.Infrastructure.Platform.Security;

namespace NomNomzBot.Infrastructure.Tests.Music;

/// <summary>
/// Live 2026-10-05: a channel's Spotify got hammered, Spotify answered <c>Retry-After</c> 8284 s, and the bot
/// kept calling. Every Spotify call (search, resolve, library writes) goes through one gate: while the
/// channel's cooldown runs, no request leaves the process, and a real 429 starts that cooldown from
/// <c>Retry-After</c>. Polly is bypassed here (<see cref="SingleHandlerClientFactory"/>), so the gate alone
/// is what is proven.
/// </summary>
public sealed class SpotifyRateLimitGateTests
{
    private static readonly Guid ChannelId = Guid.Parse("0192a000-0000-7000-8000-0000000f9002");

    private static readonly DateTimeOffset Now = new(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task A_library_write_during_the_cooldown_sends_nothing_to_spotify()
    {
        (
            MusicProviderManageApi api,
            SpotifyMusicProvider _,
            SpotifyRateLimitCooldowns cooldowns,
            RecordingHttpHandler handler
        ) = Build();
        cooldowns.CoolUntil(ChannelId, Now, Now + TimeSpan.FromMinutes(10));

        Result result = await api.SaveTracksAsync(ChannelId, "spotify", ["spotify:track:x"]);

        result.IsFailure.Should().BeTrue();
        handler.RequestUrls.Should().BeEmpty();
    }

    [Fact]
    public async Task A_real_429_starts_the_cooldown_from_retry_after()
    {
        (
            MusicProviderManageApi _,
            SpotifyMusicProvider spotify,
            SpotifyRateLimitCooldowns _,
            RecordingHttpHandler handler
        ) = Build();
        handler.RespondWhen(
            _ => true,
            HttpStatusCode.TooManyRequests,
            new Dictionary<string, string> { ["Retry-After"] = "120" }
        );

        (IReadOnlyList<TrackInfo> tracks, MusicProviderFailureReason failure) =
            await spotify.SearchAsync(ChannelId, "banger");

        tracks.Should().BeEmpty();
        failure.Should().Be(MusicProviderFailureReason.RateLimited);
        handler.RequestUrls.Should().HaveCount(1);
        spotify.TryGetCoolingUntil(ChannelId, out DateTimeOffset until).Should().BeTrue();
        until.Should().BeCloseTo(Now + TimeSpan.FromSeconds(120), TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task A_search_during_the_cooldown_reports_rate_limited_and_sends_nothing()
    {
        (
            MusicProviderManageApi _,
            SpotifyMusicProvider spotify,
            SpotifyRateLimitCooldowns cooldowns,
            RecordingHttpHandler handler
        ) = Build();
        cooldowns.CoolUntil(ChannelId, Now, Now + TimeSpan.FromMinutes(10));

        (IReadOnlyList<TrackInfo> tracks, MusicProviderFailureReason failure) =
            await spotify.SearchAsync(ChannelId, "banger");

        tracks.Should().BeEmpty();
        failure.Should().Be(MusicProviderFailureReason.RateLimited);
        handler.RequestUrls.Should().BeEmpty();
    }

    [Fact]
    public async Task A_track_resolve_during_the_cooldown_reports_rate_limited_and_sends_nothing()
    {
        (
            MusicProviderManageApi _,
            SpotifyMusicProvider spotify,
            SpotifyRateLimitCooldowns cooldowns,
            RecordingHttpHandler handler
        ) = Build();
        cooldowns.CoolUntil(ChannelId, Now, Now + TimeSpan.FromMinutes(10));

        (TrackInfo? track, MusicProviderFailureReason failure) = await spotify.ResolveTrackAsync(
            ChannelId,
            "spotify:track:4cOdK2wGLETKBW3PvgPWqT"
        );

        track.Should().BeNull();
        failure.Should().Be(MusicProviderFailureReason.RateLimited);
        handler.RequestUrls.Should().BeEmpty();
    }

    private static (
        MusicProviderManageApi Api,
        SpotifyMusicProvider Spotify,
        SpotifyRateLimitCooldowns Cooldowns,
        RecordingHttpHandler Handler
    ) Build()
    {
        MusicTestDbContext db = MusicTestDbContext.New();
        FakeIntegrationTokenVault vault = new(db);
        vault.SeedConnectedSpotify(ChannelId);

        RecordingHttpHandler handler = new();
        SpotifyRateLimitCooldowns cooldowns = new(new NullActionRequiredChangeNotifier());
        FakeTimeProvider clock = new(Now);
        SpotifyMusicProvider spotify = new(
            db,
            vault,
            new InMemoryIntegrationCapabilityStore(),
            new LastActiveSpotifyDeviceTracker(),
            cooldowns,
            new SingleHandlerClientFactory(handler),
            clock,
            NullLogger<SpotifyMusicProvider>.Instance,
            NullSystemCredentialsProvider.Instance,
            new ConnectionRefreshGate(),
            new NullChannelCredentialsResolver(NullSystemCredentialsProvider.Instance),
            new OutboundSanctionAccessor()
        );

        return (new MusicProviderManageApi([spotify]), spotify, cooldowns, handler);
    }
}
