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
using NomNomzBot.Domain.Music.Interfaces;
using NomNomzBot.Infrastructure.Identity;
using NomNomzBot.Infrastructure.Integrations;
using NomNomzBot.Infrastructure.Music;
using NomNomzBot.Infrastructure.Platform.Security;

namespace NomNomzBot.Infrastructure.Tests.Music;

/// <summary>
/// V-B5.1 — the frozen song-request-queue bug: a streamer's Spotify was rate-limited in a ~6s loop,
/// <c>FailureCount</c> never moved, and the queue stayed frozen. Two failures compounded: (1)
/// <c>SpotifyMusicProvider.SendAsync</c> retried a 429 a SECOND time on top of Polly's own retry
/// (<see cref="Platform.Resilience.ResiliencePolicies.AddSpotifyResilienceHandler"/>), so one rate-limited
/// poll could cost up to 3 real HTTP calls from this layer alone; (2) a 429 collapsed to a bare <c>null</c>,
/// indistinguishable from "nothing playing", so nothing ever remembered the channel was rate-limited.
///
/// These tests build the REAL <see cref="SpotifyMusicProvider"/> directly over a handler that returns 429
/// (skipping Polly — <see cref="SingleHandlerClientFactory"/> has no resilience pipeline, same as every
/// other Spotify provider test in this project), so they isolate the provider's OWN behavior: it must
/// retry a rate-limited call ZERO times (Polly is the only retry owner) and must record the cooldown via
/// <see cref="IMusicProvider.TryGetCoolingUntil"/> rather than only returning null.
/// </summary>
public sealed class SpotifyMusicProviderRateLimitTests
{
    private static readonly Guid ChannelId = Guid.Parse("0192a000-0000-7000-8000-0000000fb001");

    [Fact]
    public async Task A_429_on_the_now_playing_read_is_not_retried_by_the_provider_itself()
    {
        (SpotifyMusicProvider provider, RecordingHttpHandler handler, _) = Build();
        handler.RespondWhen(
            r => r.RequestUri!.AbsolutePath.EndsWith("/me/player", StringComparison.Ordinal),
            HttpStatusCode.TooManyRequests,
            // the exact header value observed live that used to cause a zero-delay, twice-retried loop
            // when this layer retried on top of Polly.
            new Dictionary<string, string> { ["Retry-After"] = "0" }
        );

        TrackInfo? track = await provider.GetCurrentTrackAsync(ChannelId);

        track
            .Should()
            .BeNull("a rate-limited read is 'cannot tell', same as any other unanswerable read");
        handler
            .RequestUrls.Should()
            .HaveCount(1, "the provider must not retry a 429 itself — that is Polly's job");
    }

    [Fact]
    public async Task A_429_on_a_player_write_is_not_retried_by_the_provider_itself()
    {
        (SpotifyMusicProvider provider, RecordingHttpHandler handler, _) = Build();
        handler.RespondWhen(
            r => r.RequestUri!.AbsolutePath.EndsWith("/me/player/pause", StringComparison.Ordinal),
            HttpStatusCode.TooManyRequests,
            new Dictionary<string, string> { ["Retry-After"] = "2" }
        );

        await provider.PauseAsync(ChannelId);

        handler
            .RequestUrls.Should()
            .HaveCount(1, "SendPlayerCommandAsync must not retry a 429 itself either");
    }

    [Fact]
    public async Task A_429_records_a_typed_cooldown_instead_of_only_returning_null()
    {
        FakeTimeProvider clock = new(DateTimeOffset.UtcNow);
        (SpotifyMusicProvider provider, RecordingHttpHandler handler, _) = Build(clock);
        handler.RespondWhen(
            r => r.RequestUri!.AbsolutePath.EndsWith("/me/player", StringComparison.Ordinal),
            HttpStatusCode.TooManyRequests,
            new Dictionary<string, string> { ["Retry-After"] = "5" }
        );

        await provider.GetCurrentTrackAsync(ChannelId);

        provider
            .TryGetCoolingUntil(ChannelId, out DateTimeOffset until)
            .Should()
            .BeTrue(
                "the provider must remember the rate limit, not just answer null this one time"
            );
        until
            .Should()
            .Be(clock.GetUtcNow() + TimeSpan.FromSeconds(5), "the deadline honors Retry-After");
    }

    /// <summary>A missing/zero Retry-After must still cool down for a sane floor, never "clear instantly" —
    /// the exact header value ("0") observed live that drove the zero-delay retry storm.</summary>
    [Fact]
    public async Task A_zero_retry_after_still_floors_the_cooldown_to_at_least_one_second()
    {
        FakeTimeProvider clock = new(DateTimeOffset.UtcNow);
        (SpotifyMusicProvider provider, RecordingHttpHandler handler, _) = Build(clock);
        handler.RespondWhen(
            r => r.RequestUri!.AbsolutePath.EndsWith("/me/player", StringComparison.Ordinal),
            HttpStatusCode.TooManyRequests,
            new Dictionary<string, string> { ["Retry-After"] = "0" }
        );

        await provider.GetCurrentTrackAsync(ChannelId);

        provider.TryGetCoolingUntil(ChannelId, out DateTimeOffset until).Should().BeTrue();
        until
            .Should()
            .BeOnOrAfter(
                clock.GetUtcNow() + TimeSpan.FromSeconds(1),
                "Retry-After: 0 must never read as 'clear immediately'"
            );
    }

    /// <summary>The cooldown clears itself once its deadline passes — <see cref="IMusicProvider.TryGetCoolingUntil"/>
    /// is a live check against the clock, not a one-shot flag.</summary>
    [Fact]
    public async Task The_cooldown_clears_once_its_deadline_passes()
    {
        FakeTimeProvider clock = new(DateTimeOffset.UtcNow);
        (SpotifyMusicProvider provider, RecordingHttpHandler handler, _) = Build(clock);
        handler.RespondWhen(
            r => r.RequestUri!.AbsolutePath.EndsWith("/me/player", StringComparison.Ordinal),
            HttpStatusCode.TooManyRequests,
            new Dictionary<string, string> { ["Retry-After"] = "3" }
        );

        await provider.GetCurrentTrackAsync(ChannelId);
        provider.TryGetCoolingUntil(ChannelId, out _).Should().BeTrue();

        clock.Advance(TimeSpan.FromSeconds(3).Add(TimeSpan.FromMilliseconds(1)));

        provider
            .TryGetCoolingUntil(ChannelId, out _)
            .Should()
            .BeFalse("once the deadline has passed the channel is no longer cooling");
    }

    /// <summary>
    /// Production 2026-09-29: Spotify answered one channel with a ~16.5 hour Retry-After and the bot kept calling
    /// for all of it (16,321 429s in a day). The provider is scoped, so the cooldown must outlive the instance
    /// that recorded it: the next scope (the next poll tick, the next dashboard request) must not call Spotify.
    /// </summary>
    [Fact]
    public async Task A_cooldown_recorded_in_one_scope_keeps_the_next_scope_from_calling_Spotify()
    {
        FakeTimeProvider clock = new(DateTimeOffset.UtcNow);
        SpotifyRateLimitCooldowns cooldowns = new();
        (SpotifyMusicProvider first, RecordingHttpHandler firstHandler, _) = Build(
            clock,
            cooldowns
        );
        firstHandler.RespondWhen(
            r => r.RequestUri!.AbsolutePath.EndsWith("/me/player", StringComparison.Ordinal),
            HttpStatusCode.TooManyRequests,
            new Dictionary<string, string> { ["Retry-After"] = "60" }
        );
        await first.GetCurrentTrackAsync(ChannelId);

        (SpotifyMusicProvider next, RecordingHttpHandler nextHandler, _) = Build(clock, cooldowns);
        TrackInfo? read = await next.GetCurrentTrackAsync(ChannelId);
        await next.PlayAsync(ChannelId);

        read.Should().BeNull();
        nextHandler
            .RequestUrls.Should()
            .NotContain(
                url => url.Contains("/me/player", StringComparison.Ordinal),
                "a channel Spotify told to wait gets no call at all until Retry-After has passed"
            );
        next.TryGetCoolingUntil(ChannelId, out _).Should().BeTrue();
    }

    [Fact]
    public async Task Once_the_shared_cooldown_passes_the_next_scope_calls_Spotify_again()
    {
        FakeTimeProvider clock = new(DateTimeOffset.UtcNow);
        SpotifyRateLimitCooldowns cooldowns = new();
        (SpotifyMusicProvider first, RecordingHttpHandler firstHandler, _) = Build(
            clock,
            cooldowns
        );
        firstHandler.RespondWhen(
            r => r.RequestUri!.AbsolutePath.EndsWith("/me/player", StringComparison.Ordinal),
            HttpStatusCode.TooManyRequests,
            new Dictionary<string, string> { ["Retry-After"] = "5" }
        );
        await first.GetCurrentTrackAsync(ChannelId);

        clock.Advance(TimeSpan.FromSeconds(6));
        (SpotifyMusicProvider next, RecordingHttpHandler nextHandler, _) = Build(clock, cooldowns);
        nextHandler.RespondWhen(
            r => r.RequestUri!.AbsolutePath.EndsWith("/me/player", StringComparison.Ordinal),
            HttpStatusCode.OK,
            """{"is_playing":false}"""
        );
        await next.GetCurrentTrackAsync(ChannelId);

        nextHandler
            .RequestUrls.Should()
            .Contain(url => url.Contains("/me/player", StringComparison.Ordinal));
    }

    /// <summary>A channel never rate-limited never reports a cooldown — the default posture.</summary>
    [Fact]
    public async Task A_healthy_channel_never_reports_a_cooldown()
    {
        (SpotifyMusicProvider provider, RecordingHttpHandler handler, _) = Build();
        handler.RespondWhen(
            r => r.RequestUri!.AbsolutePath.EndsWith("/me/player", StringComparison.Ordinal),
            HttpStatusCode.OK,
            """{"is_playing":false}"""
        );

        await provider.GetCurrentTrackAsync(ChannelId);

        provider.TryGetCoolingUntil(ChannelId, out _).Should().BeFalse();
    }

    // ─── Harness ──────────────────────────────────────────────────────────────

    private static (
        SpotifyMusicProvider Provider,
        RecordingHttpHandler Handler,
        InMemoryIntegrationCapabilityStore Store
    ) Build(TimeProvider? timeProvider = null, SpotifyRateLimitCooldowns? cooldowns = null)
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
        InMemoryIntegrationCapabilityStore store = new();
        SpotifyMusicProvider provider = new(
            db,
            vault,
            store,
            new LastActiveSpotifyDeviceTracker(),
            cooldowns ?? new SpotifyRateLimitCooldowns(),
            new SingleHandlerClientFactory(handler),
            timeProvider ?? TimeProvider.System,
            NullLogger<SpotifyMusicProvider>.Instance,
            NullSystemCredentialsProvider.Instance,
            new ConnectionRefreshGate(),
            new NullChannelCredentialsResolver(NullSystemCredentialsProvider.Instance),
            new OutboundSanctionAccessor()
        );

        return (provider, handler, store);
    }
}
