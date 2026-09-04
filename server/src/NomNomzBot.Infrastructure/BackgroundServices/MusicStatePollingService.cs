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
using NomNomzBot.Application.Contracts.Music;
using NomNomzBot.Application.Music.Services;
using NomNomzBot.Domain.Music.Events;
using NomNomzBot.Domain.Music.Interfaces;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Music;

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
/// <b>Cadence — flat 1s, not connection-aware.</b> The rails asked for a livelier ~5s cadence while a
/// dashboard/overlay client is plausibly connected, backing off to ~30-60s otherwise, IF connection-awareness is
/// cheap to detect via the hub group registry. It is not cheap here: the only connection registry is
/// <c>DashboardHub</c>'s connection→channel map, which lives in <c>NomNomzBot.Api</c> — a project this
/// (Infrastructure-layer) poller must not reference without inverting Clean Architecture's inward-only
/// dependency rule. Exposing that registry through a new Application-layer seam just for this cadence hint is a
/// bigger seam than the poller warrants (YAGNI) versus a single safety-first flat cadence. 1s is what "no more
/// than 1 second of drift from the real Spotify state" (owner requirement) actually costs: a change the bot
/// didn't cause (streamer pauses from their phone, a track ends, a manual seek) can only ever be as fresh as this
/// tick, since it's the only thing that notices it. A flat per-channel 1s cadence is safe to keep simple here
/// because the actual Spotify budget concern — it IS app-wide (per <c>client_id</c>, shared across every
/// connected channel's token, rolling 30s window: developer.spotify.com/documentation/web-api/concepts/rate-
/// limits) — is enforced once, centrally, at the HTTP layer
/// (<see cref="Platform.Resilience.ResiliencePolicies.AddSpotifyResilienceHandler"/>), not per caller. This
/// poller does not need to reason about channel count itself; per-channel failures back off further below so a
/// struggling channel doesn't hammer a dead token every second.
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
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan BackoffBase = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan BackoffCap = TimeSpan.FromMinutes(5);

    // A "seek" is flagged when observed progress diverges from the time-elapsed-implied progress by more than
    // this, while track + play state are otherwise unchanged. At a 1s poll interval this only needs to absorb
    // ordinary network/scheduling jitter between ticks, not multi-second slack — a genuine seek is still many
    // times larger than this.
    internal const int SeekDriftToleranceMs = 750;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IEventBus _eventBus;
    private readonly TimeProvider _timeProvider;
    private readonly IMusicRealtimeSignal _realtime;
    private readonly ILogger<MusicStatePollingService> _logger;

    private readonly ConcurrentDictionary<Guid, ChannelPlaybackSnapshot> _lastState = new();
    private readonly ConcurrentDictionary<Guid, ChannelBackoff> _backoff = new();

    public MusicStatePollingService(
        IServiceScopeFactory scopeFactory,
        IEventBus eventBus,
        TimeProvider timeProvider,
        IMusicRealtimeSignal realtime,
        ILogger<MusicStatePollingService> logger
    )
    {
        _scopeFactory = scopeFactory;
        _eventBus = eventBus;
        _timeProvider = timeProvider;
        _realtime = realtime;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "MusicStatePollingService starting (flat {IntervalSeconds}s cadence).",
            PollInterval.TotalSeconds
        );

        using PeriodicTimer timer = new(PollInterval, _timeProvider);
        // Only ONE outstanding WaitForNextTickAsync is allowed at a time, so the pending tick is held across
        // iterations: when a realtime nudge wins the race the tick stays armed rather than being re-requested.
        Task<bool> tick = timer.WaitForNextTickAsync(stoppingToken).AsTask();

        while (true)
        {
            try
            {
                await PollAllChannelsOnceAsync(stoppingToken);
            }
            // Filter on the STOPPING TOKEN, not on the exception type. `ex is not
            // OperationCanceledException` looks like "let shutdown through", but TaskCanceledException
            // DERIVES from OperationCanceledException — and HttpClient raises exactly that on its 100s
            // timeout. So a single slow Spotify call escaped this catch, and with
            // BackgroundServiceExceptionBehavior.StopHost that took the WHOLE BOT down: 5 crash-loop
            // restarts and a 502 dashboard on 2026-08-25. Only a genuinely cancelled stoppingToken
            // means "we are shutting down"; everything else is a tick failure to log and survive.
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogError(ex, "MusicStatePollingService: tick failed");
            }

            // Whichever comes first: the 1s floor, or a provider's realtime transport saying playback just
            // changed. The nudge is what closes the gap the owner sees on the overlay — a track change used
            // to wait out a full poll interval plus the widget's own interpolation before it showed.
            Task nudge = _realtime.WaitForNudgeAsync(stoppingToken);
            Task winner;
            try
            {
                winner = await Task.WhenAny(tick, nudge);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }

            if (winner != tick)
                continue; // Nudged: poll now, and leave the timer armed for its own tick.

            // The timer returns false only when it is disposed or the token fired — either way, stop.
            if (!await tick)
                return;

            tick = timer.WaitForNextTickAsync(stoppingToken).AsTask();
        }
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
        ISongRequestHandover handover =
            scope.ServiceProvider.GetRequiredService<ISongRequestHandover>();
        List<string> providerKeys =
        [
            .. scope.ServiceProvider.GetServices<IMusicProvider>().Select(p => p.Provider),
        ];

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

                // Recovery tick for a stuck song-request queue. SongRequestQueueReconciler advances the
                // queue off playback CHANGES, but a handover that could not land — the streamer's player
                // was closed, the token had died, a transient provider error — leaves requests waiting
                // with nothing in flight, and a paused or unchanging player publishes no further change
                // to wake the reconciler. Asking here every tick means the queue resumes by itself the
                // moment playback is possible again, instead of waiting for a viewer to request another
                // song. No-ops when something is already in flight or the queue is empty.
                await handover.HandOverNextAsync(channelId.ToString(), cancellationToken);
            }
            // Same reasoning as the tick catch above, and it matters twice over here: a provider
            // HttpClient timeout surfaces as TaskCanceledException, so the old type filter let ONE
            // channel's slow Spotify call abort the sweep for every OTHER channel on that tick.
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
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
            ? new(false, null, 0, 100, observedAt, true, true, true, true, true, true, true)
            : new ChannelPlaybackSnapshot(
                nowPlaying.IsPlaying,
                nowPlaying.TrackName,
                nowPlaying.ProgressMs,
                nowPlaying.Volume,
                observedAt,
                nowPlaying.CanSetShuffle,
                nowPlaying.CanSetRepeat,
                nowPlaying.CanSkipNext,
                nowPlaying.CanSkipPrevious,
                nowPlaying.CanSeek,
                nowPlaying.CanPause,
                nowPlaying.CanResume
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
                Artist = nowPlaying?.Artist,
                Album = nowPlaying?.Album,
                AlbumArtUrl = nowPlaying?.ImageUrl,
                DurationMs = nowPlaying?.DurationMs ?? 0,
                ProgressMs = next.ProgressMs,
                Provider = nowPlaying?.Provider,
                TrackUri = nowPlaying?.TrackUri,
                ArtistId = nowPlaying?.ArtistId,
                RequestedBy = nowPlaying?.RequestedBy,
                ShuffleEnabled = nowPlaying?.ShuffleEnabled ?? false,
                RepeatMode = nowPlaying?.RepeatMode ?? MusicRepeatMode.Off,
                VolumePercent = next.VolumePercent,
                ObservedAt = observedAt,
                CanSetShuffle = next.CanSetShuffle,
                CanSetRepeat = next.CanSetRepeat,
                CanSkipNext = next.CanSkipNext,
                CanSkipPrevious = next.CanSkipPrevious,
                CanSeek = next.CanSeek,
                CanPause = next.CanPause,
                CanResume = next.CanResume,
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

        // A volume change (streamer's phone, hardware knob, another app) is otherwise invisible to every
        // push-driven consumer — nothing about track/play-state/seek reflects it — so it needs its own
        // explicit check rather than falling out of the checks above.
        if (previous.VolumePercent != next.VolumePercent)
            return true;

        // A control permission can flip mid-track with nothing else changing (an ad break blocks skip/seek,
        // a restricted market blocks shuffle, …) — otherwise invisible to every check above.
        if (
            previous.CanSetShuffle != next.CanSetShuffle
            || previous.CanSetRepeat != next.CanSetRepeat
            || previous.CanSkipNext != next.CanSkipNext
            || previous.CanSkipPrevious != next.CanSkipPrevious
            || previous.CanSeek != next.CanSeek
            || previous.CanPause != next.CanPause
            || previous.CanResume != next.CanResume
        )
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

        _backoff[channelId] = new(failures, now + delay);

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
        int VolumePercent,
        DateTimeOffset ObservedAt,
        bool CanSetShuffle,
        bool CanSetRepeat,
        bool CanSkipNext,
        bool CanSkipPrevious,
        bool CanSeek,
        bool CanPause,
        bool CanResume
    );

    /// <summary>Per-channel failure backoff state, kept in memory only.</summary>
    private sealed record ChannelBackoff(
        int ConsecutiveFailures,
        DateTimeOffset NextEligiblePollAt
    );
}
