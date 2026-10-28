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

using System.Collections.Concurrent;
using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Domain.Music.Interfaces;
using NomNomzBot.Infrastructure.Identity;
using NomNomzBot.Infrastructure.Integrations;
using NomNomzBot.Infrastructure.Music;
using NomNomzBot.Infrastructure.Notifications;
using NomNomzBot.Infrastructure.Platform.Security;

namespace NomNomzBot.Infrastructure.Tests.Music;

/// <summary>
/// A refused now-playing read (401/403/429) used to collapse to a silent <c>null</c> in
/// <c>GetCurrentTrackAsync</c>, so a dead token looked exactly like "nothing playing" in the logs.
/// These tests pin that the refusal is logged with the status code and Spotify's own error message
/// (never the token), at most once per channel per status per window, and that the integration's
/// visible auth flag is set so the dashboard Integrations page shows it.
/// </summary>
public sealed class SpotifyMusicProviderRefusedReadTests
{
    private const string Token = "test-access-token";

    [Fact]
    public async Task A_401_read_is_logged_with_status_and_spotify_message_and_flags_reauth()
    {
        Guid channelId = Guid.NewGuid();
        FakeTimeProvider clock = new(DateTimeOffset.UtcNow);
        (
            SpotifyMusicProvider provider,
            RecordingHttpHandler handler,
            InMemoryIntegrationCapabilityStore store,
            CapturingLogger logger
        ) = Build(channelId, clock);
        handler.RespondWhen(
            IsPlayerRead,
            HttpStatusCode.Unauthorized,
            """{"error":{"status":401,"message":"The access token expired"}}"""
        );

        TrackInfo? track = await provider.GetCurrentTrackAsync(channelId);

        track.Should().BeNull();
        (LogLevel Level, string Message) entry = logger.Entries.Should().ContainSingle().Subject;
        entry.Level.Should().Be(LogLevel.Warning);
        entry.Message.Should().Contain("401").And.Contain("The access token expired");
        entry.Message.Should().NotContain(Token);
        store
            .GetObserved(channelId, "spotify")[SpotifyMusicProvider.NeedsReauthCapabilityKey]
            .Should()
            .BeTrue();
    }

    [Fact]
    public async Task A_403_read_is_logged_with_its_message_and_flags_forbidden()
    {
        Guid channelId = Guid.NewGuid();
        (
            SpotifyMusicProvider provider,
            RecordingHttpHandler handler,
            InMemoryIntegrationCapabilityStore store,
            CapturingLogger logger
        ) = Build(channelId, new FakeTimeProvider(DateTimeOffset.UtcNow));
        handler.RespondWhen(
            IsPlayerRead,
            HttpStatusCode.Forbidden,
            """{"error":{"status":403,"message":"Insufficient client scope"}}"""
        );

        await provider.GetCurrentTrackAsync(channelId);

        (LogLevel Level, string Message) entry = logger.Entries.Should().ContainSingle().Subject;
        entry.Message.Should().Contain("403").And.Contain("Insufficient client scope");
        store
            .GetObserved(channelId, "spotify")[SpotifyMusicProvider.ForbiddenCapabilityKey]
            .Should()
            .BeTrue();
    }

    [Fact]
    public async Task A_429_read_is_logged_with_its_status()
    {
        // Fixed id and clock: the cooldown warning prints both, and random digits could spell "429" by chance.
        Guid channelId = Guid.Parse("11111111-2222-3333-5555-666666666666");
        (SpotifyMusicProvider provider, RecordingHttpHandler handler, _, CapturingLogger logger) =
            Build(
                channelId,
                new FakeTimeProvider(new DateTimeOffset(2026, 10, 27, 12, 0, 0, TimeSpan.Zero))
            );
        handler.RespondWhen(IsPlayerRead, HttpStatusCode.TooManyRequests, "{}");

        await provider.GetCurrentTrackAsync(channelId);

        logger
            .Entries.Should()
            .ContainSingle(e => e.Level == LogLevel.Warning && e.Message.Contains("429"));
    }

    [Fact]
    public async Task The_same_refusal_logs_once_per_window_then_again_after_it()
    {
        Guid channelId = Guid.NewGuid();
        FakeTimeProvider clock = new(DateTimeOffset.UtcNow);
        (SpotifyMusicProvider provider, RecordingHttpHandler handler, _, CapturingLogger logger) =
            Build(channelId, clock);
        handler.RespondWhen(
            IsPlayerRead,
            HttpStatusCode.Unauthorized,
            """{"error":{"status":401,"message":"The access token expired"}}"""
        );

        await provider.GetCurrentTrackAsync(channelId);
        await provider.GetCurrentTrackAsync(channelId);
        logger.Entries.Should().HaveCount(1);

        clock.Advance(TimeSpan.FromMinutes(6));
        await provider.GetCurrentTrackAsync(channelId);
        logger.Entries.Should().HaveCount(2);
    }

    private static bool IsPlayerRead(HttpRequestMessage r) =>
        r.Method == HttpMethod.Get
        && r.RequestUri!.AbsolutePath.EndsWith("/me/player", StringComparison.Ordinal);

    private static (
        SpotifyMusicProvider Provider,
        RecordingHttpHandler Handler,
        InMemoryIntegrationCapabilityStore Store,
        CapturingLogger Logger
    ) Build(Guid channelId, TimeProvider timeProvider)
    {
        MusicTestDbContext db = MusicTestDbContext.New();
        db.Services.Add(
            new()
            {
                Id = Guid.NewGuid().ToString(),
                Name = "spotify",
                BroadcasterId = channelId,
                Enabled = true,
                AccessToken = Token,
            }
        );
        db.SaveChanges();

        FakeIntegrationTokenVault vault = new(db);
        vault.SeedConnectedSpotify(channelId);

        RecordingHttpHandler handler = new();
        InMemoryIntegrationCapabilityStore store = new();
        CapturingLogger logger = new();
        SpotifyMusicProvider provider = new(
            db,
            vault,
            store,
            new LastActiveSpotifyDeviceTracker(),
            new SpotifyRateLimitCooldowns(new NullActionRequiredChangeNotifier()),
            new SingleHandlerClientFactory(handler),
            timeProvider,
            logger,
            NullSystemCredentialsProvider.Instance,
            new ConnectionRefreshGate(),
            new NullChannelCredentialsResolver(NullSystemCredentialsProvider.Instance),
            new OutboundSanctionAccessor()
        );

        return (provider, handler, store, logger);
    }

    /// <summary>Records every warning-or-worse entry the provider writes.</summary>
    private sealed class CapturingLogger : ILogger<SpotifyMusicProvider>
    {
        private readonly ConcurrentQueue<(LogLevel Level, string Message)> _entries = new();

        public IReadOnlyCollection<(LogLevel Level, string Message)> Entries => _entries;

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        )
        {
            if (logLevel >= LogLevel.Warning)
                _entries.Enqueue((logLevel, formatter(state, exception)));
        }
    }
}
