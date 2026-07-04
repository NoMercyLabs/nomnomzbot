// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Music.Services;
using NomNomzBot.Domain.Music.Events;
using NomNomzBot.Domain.Music.Interfaces;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Infrastructure.BackgroundServices;

/// <summary>
/// Polls every channel with a connected music integration for playback state and publishes
/// <see cref="PlaybackStateChangedEvent"/> whenever the state actually changed, so the dashboard's music panel
/// (<c>PlaybackStateBroadcastHandler</c> → hub <c>MusicStateChanged</c>) and the overlay now-playing widget
/// (<c>WidgetNowPlayingHandler</c>) stop being pull-only/stale. Mutation-path actions (play/pause/skip/
/// play-context, <see cref="NomNomzBot.Infrastructure.Music.MusicService"/>) already publish the same event the
/// instant they succeed — this poller exists for state changes the bot didn't cause: Spotify controlled from the
/// streamer's own phone/desktop app, a track ending naturally, or a manual seek.
///
/// <para>
/// <b>Cadence — flat 10s, not connection-aware.</b> The rails asked for a livelier ~5s cadence while a
/// dashboard/overlay client is plausibly connected, backing off to ~30-60s otherwise, IF connection-awareness is
/// cheap to detect via the hub group registry. It is not cheap here: the only connection registry is
/// <c>DashboardHub</c>'s connection→channel map, which lives in <c>NomNomzBot.Api</c> — a project this
/// (Infrastructure-layer) poller must not reference without inverting Clean Architecture's inward-only
/// dependency rule. Exposing that registry through a new Application-layer seam just for this cadence hint is a
/// bigger seam than the poller warrants (YAGNI) versus a single safety-first flat cadence. 10s keeps Spotify Web
/// API usage modest per channel (a "currently playing" read is a single lightweight per-user-token call, not
/// app-wide-quota'd) while still feeling responsive; per-channel failures back off further below.
/// </para>
///
/// <para>
/// <b>State-change detection.</b> Per channel, in memory only (no DB writes — rail requirement): a track change,
/// a play/pause flip, or a "seek" (observed progress diverging from elapsed-time-implied progress by more than
/// <see cref="SeekDriftToleranceMs"/> while track + play state are otherwise unchanged) triggers a publish. The
/// very first observation of a channel always publishes once, establishing the dashboard's baseline instead of
/// waiting for the next real change.
/// </para>
///
/// <para>
/// <b>Resilience.</b> Each channel is polled independently inside its own try/catch — one channel's exception
/// (expired token, transient 429, etc.) never stops the others or crashes the loop. A channel that just failed is
/// skipped (silently — no logspam) until a capped exponential backoff window elapses, then retried.
/// </para>
/// </summary>
public sealed class MusicStatePollingService : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan BackoffBase = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan BackoffCap = TimeSpan.FromMinutes(5);

    // A "seek" is flagged when observed progress diverges from the time-elapsed-implied progress by more than
    // this, while track + play state are otherwise unchanged. Half the poll interval's worth of slack absorbs
    // ordinary tick jitter without masking a genuine seek (which is typically many seconds).
    internal const int SeekDriftToleranceMs = 5_000;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IEventBus _eventBus;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<MusicStatePollingService> _logger;

    private readonly ConcurrentDictionary<Guid, ChannelPlaybackSnapshot> _lastState = new();
    private readonly ConcurrentDictionary<Guid, ChannelBackoff> _backoff = new();

    public MusicStatePollingService(
        IServiceScopeFactory scopeFactory,
        IEventBus eventBus,
        TimeProvider timeProvider,
        ILogger<MusicStatePollingService> logger
    )
    {
        _scopeFactory = scopeFactory;
        _eventBus = eventBus;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "MusicStatePollingService starting (flat {IntervalSeconds}s cadence).",
            PollInterval.TotalSeconds
        );

        using PeriodicTimer timer = new(PollInterval, _timeProvider);
        do
        {
            try
            {
                await PollAllChannelsOnceAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "MusicStatePollingService: tick failed");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    /// <summary>
    /// Runs one full poll pass over every channel with a connected music integration. Internal (not private) so
    /// tests can drive discrete ticks directly instead of waiting on the real <see cref="PeriodicTimer"/>.
    /// </summary>
    internal async Task PollAllChannelsOnceAsync(CancellationToken cancellationToken)
    {
        using IServiceScope scope = _scopeFactory.CreateScope();
        IApplicationDbContext db =
            scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        IMusicService musicService = scope.ServiceProvider.GetRequiredService<IMusicService>();
        List<string> providerKeys = scope
            .ServiceProvider.GetServices<IMusicProvider>()
            .Select(p => p.Provider)
            .ToList();

        List<Guid> channelIds = await LoadConnectedChannelsAsync(
            db,
            providerKeys,
            cancellationToken
        );
        if (channelIds.Count == 0)
            return;

        DateTimeOffset now = _timeProvider.GetUtcNow();

        foreach (Guid channelId in channelIds)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            if (
                _backoff.TryGetValue(channelId, out ChannelBackoff? backoff)
                && now < backoff.NextEligiblePollAt
            )
                continue; // Still cooling down after a recent failure — skip silently, no logspam.

            try
            {
                NowPlaying? nowPlaying = await musicService.GetNowPlayingAsync(
                    channelId.ToString(),
                    cancellationToken
                );
                _backoff.TryRemove(channelId, out _);
                await ProcessChannelStateAsync(channelId, nowPlaying, now, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                RecordFailure(channelId, now, ex);
            }
        }
    }

    /// <summary>Every channel with an enabled, token-bearing connection to a <b>registered music provider</b>
    /// — the same connected-names ∩ registered-provider-keys eligibility
    /// <see cref="NomNomzBot.Infrastructure.Music.MusicService"/> applies when resolving the active provider,
    /// so "connected" means the same thing here as it does everywhere else in the product (and a newly
    /// registered provider is picked up without touching this poller).</summary>
    private static async Task<List<Guid>> LoadConnectedChannelsAsync(
        IApplicationDbContext db,
        List<string> providerKeys,
        CancellationToken cancellationToken
    ) =>
        await db
            .Services.Where(s =>
                s.BroadcasterId != null
                && s.Enabled
                && s.AccessToken != null
                && providerKeys.Contains(s.Name)
            )
            .Select(s => s.BroadcasterId!.Value)
            .Distinct()
            .ToListAsync(cancellationToken);

    private async Task ProcessChannelStateAsync(
        Guid channelId,
        NowPlaying? nowPlaying,
        DateTimeOffset observedAt,
        CancellationToken cancellationToken
    )
    {
        ChannelPlaybackSnapshot next = nowPlaying is null
            ? new ChannelPlaybackSnapshot(false, null, 0, observedAt)
            : new ChannelPlaybackSnapshot(
                nowPlaying.IsPlaying,
                nowPlaying.TrackName,
                nowPlaying.ProgressMs,
                observedAt
            );

        bool changed =
            !_lastState.TryGetValue(channelId, out ChannelPlaybackSnapshot? previous)
            // First observation for this channel: publish once to establish the dashboard's baseline.
            || HasChanged(previous, next);

        _lastState[channelId] = next;

        if (!changed)
            return;

        await _eventBus.PublishAsync(
            new PlaybackStateChangedEvent
            {
                BroadcasterId = channelId,
                IsPlaying = next.IsPlaying,
                TrackName = next.TrackName,
            },
            cancellationToken
        );
    }

    private static bool HasChanged(ChannelPlaybackSnapshot previous, ChannelPlaybackSnapshot next)
    {
        if (previous.TrackName != next.TrackName)
            return true;

        if (previous.IsPlaying != next.IsPlaying)
            return true;

        // A seek only makes sense to check while the same track keeps playing across both observations —
        // otherwise the track-change/play-flip branches above already cover it.
        if (!next.IsPlaying || !previous.IsPlaying)
            return false;

        double elapsedMs = (next.ObservedAt - previous.ObservedAt).TotalMilliseconds;
        double expectedProgressMs = previous.ProgressMs + elapsedMs;
        double drift = Math.Abs(next.ProgressMs - expectedProgressMs);
        return drift > SeekDriftToleranceMs;
    }

    private void RecordFailure(Guid channelId, DateTimeOffset now, Exception ex)
    {
        int failures =
            (
                _backoff.TryGetValue(channelId, out ChannelBackoff? existing)
                    ? existing.ConsecutiveFailures
                    : 0
            ) + 1;

        double cappedDelayMs = Math.Min(
            BackoffCap.TotalMilliseconds,
            BackoffBase.TotalMilliseconds * Math.Pow(2, failures - 1)
        );
        TimeSpan delay = TimeSpan.FromMilliseconds(cappedDelayMs);

        _backoff[channelId] = new ChannelBackoff(failures, now + delay);

        _logger.LogWarning(
            ex,
            "MusicStatePollingService: poll failed for channel {ChannelId} (attempt {Attempt}) — backing off {DelaySeconds}s",
            channelId,
            failures,
            delay.TotalSeconds
        );
    }

    /// <summary>The last observed playback state for one channel, kept in memory only.</summary>
    private sealed record ChannelPlaybackSnapshot(
        bool IsPlaying,
        string? TrackName,
        int ProgressMs,
        DateTimeOffset ObservedAt
    );

    /// <summary>Per-channel failure backoff state, kept in memory only.</summary>
    private sealed record ChannelBackoff(
        int ConsecutiveFailures,
        DateTimeOffset NextEligiblePollAt
    );
}
