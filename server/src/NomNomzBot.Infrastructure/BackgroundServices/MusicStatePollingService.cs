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
using NomNomzBot.Domain.Identity.Enums;
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
/// <b>Cadence — 1s while it matters, 60s while it doesn't.</b> A channel is polled at the flat 1s cadence
/// (<see cref="PollInterval"/> — "no more than 1 second of drift from the real Spotify state", owner
/// requirement) exactly while it is live OR at least one consumer is actually connected and watching: a
/// dashboard music panel subscribed to the <c>music</c> push class (<c>DashboardHub</c>), an overlay
/// now-playing widget (<c>OverlayHub</c>), or a Stream Deck/Automation API client subscribed to
/// <c>song.changed</c> (<c>AutomationStreamCoordinator</c>) — see <see cref="IChannelRegistry.HasMusicDemand"/>,
/// updated directly by each of those (all three already sit outward of this Infrastructure-layer poller in
/// Clean Architecture's dependency order, so none of them need a new seam to update the SAME Domain-level
/// registry this poller already reads for <see cref="ChannelContext.IsLive"/>). Otherwise it coasts at
/// <see cref="IdlePollInterval"/> — still catches an out-of-band change (phone, another app) within a minute,
/// without spending the shared Spotify budget on a channel nobody is watching. The Spotify budget itself
/// remains enforced once, centrally, at the HTTP layer
/// (<see cref="Platform.Resilience.ResiliencePolicies.AddSpotifyResilienceHandler"/>), split into a poll
/// partition and an interactive (user-triggered) partition so a busy poller can never starve a real pause/
/// skip/search — this poller tags every call it makes as background (<c>isBackgroundPoll: true</c>) so it
/// only ever draws from its own partition. Per-channel failures back off further below so a struggling
/// channel doesn't hammer a dead token every second.
/// </para>
///
/// <para>
/// <b>State-change detection.</b> Per channel, in memory only (no DB writes — rail requirement): a track change,
/// a play/pause flip, or a "seek" (observed progress diverging from elapsed-time-implied progress by more than
/// <see cref="SeekDriftToleranceMs"/> while track + play state are otherwise unchanged) triggers a publish. The
/// very first observation of a channel always publishes once, establishing the dashboard's baseline instead of
/// waiting for the next real change. A provider reporting "nothing playing" (no active device) only counts as a
/// real stop after <see cref="NullConfirmationTicks"/> consecutive ticks agree — see its own doc comment.
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

    // The bare-minimum cadence for a channel nobody is watching right now: offline AND no dashboard music
    // panel / overlay now-playing widget / Stream Deck song.changed subscriber currently connected
    // (IChannelRegistry.HasMusicDemand). Still catches a state change from outside the bot (phone, another
    // app) within a minute, without holding the full 1s-per-channel Spotify budget for a channel with nobody
    // to show the answer to — see ResiliencePolicies.AddSpotifyResilienceHandler for the budget this frees.
    private static readonly TimeSpan IdlePollInterval = TimeSpan.FromSeconds(60);

    // The cadence for a WATCHED channel whose player is not actually playing anything. Being watched is not the
    // same as having something to report: a live channel with the music paused answered 204 "nothing playing" on
    // every one of its 1s ticks, and that is where the Spotify budget went — 2348 of 3776 calls in one 30-minute
    // run (62%) returned 204, while 948 (25%) came back 429 because of the volume (measured on the deployed box
    // 2026-09-21). Spotify's limit is per-app, so on the hosted profile every channel's idle polling is spent
    // out of the SAME budget a playing channel needs. Polling a silent player five times slower costs at most
    // this long to notice playback starting, and hands that budget back to the channels actually playing.
    private static readonly TimeSpan QuietPollInterval = TimeSpan.FromSeconds(5);

    // A "seek" is flagged when observed progress diverges from the time-elapsed-implied progress by more than
    // this, while track + play state are otherwise unchanged. At a 1s poll interval this only needs to absorb
    // ordinary network/scheduling jitter between ticks, not multi-second slack — a genuine seek is still many
    // times larger than this.
    internal const int SeekDriftToleranceMs = 750;

    // A provider returning "nothing playing" (Spotify: 204/NO_ACTIVE_DEVICE) is not always a real stop — some
    // Spotify Connect playback targets (smart TVs in particular) drop their device-presence heartbeat for a tick
    // or two while genuinely still playing. Treating the FIRST such null as an authoritative stop published a
    // real is-playing:false, then the very next tick published is-playing:true again the moment the target's
    // heartbeat came back — the overlay widget saw this as isPlaying flapping and its progress bar snapping to 0
    // and back. Requiring the null to repeat for this many consecutive ticks before it counts as a real stop
    // absorbs a one-tick blip; a genuine pause/stop is still reflected within a couple of seconds.
    internal const int NullConfirmationTicks = 2;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IEventBus _eventBus;
    private readonly TimeProvider _timeProvider;
    private readonly IChannelRegistry _channelRegistry;
    private readonly ILogger<MusicStatePollingService> _logger;

    private readonly ConcurrentDictionary<Guid, ChannelPlaybackSnapshot> _lastState = new();
    private readonly ConcurrentDictionary<Guid, ChannelBackoff> _backoff = new();
    private readonly ConcurrentDictionary<Guid, int> _consecutiveNullPolls = new();
    private readonly ConcurrentDictionary<Guid, DateTimeOffset> _lastPolledAt = new();

    public MusicStatePollingService(
        IServiceScopeFactory scopeFactory,
        IEventBus eventBus,
        TimeProvider timeProvider,
        IChannelRegistry channelRegistry,
        ILogger<MusicStatePollingService> logger
    )
    {
        _scopeFactory = scopeFactory;
        _eventBus = eventBus;
        _timeProvider = timeProvider;
        _channelRegistry = channelRegistry;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "MusicStatePollingService starting ({FastSeconds}s while live/watched, {IdleSeconds}s otherwise).",
            PollInterval.TotalSeconds,
            IdlePollInterval.TotalSeconds
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

            // The timer returns false only when it is disposed or the token fired — either way, stop.
            bool ticked;
            try
            {
                ticked = await tick;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }

            if (!ticked)
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
        List<IMusicProvider> providers = [.. scope.ServiceProvider.GetServices<IMusicProvider>()];
        List<string> providerKeys = [.. providers.Select(p => p.Provider)];

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

            if (!IsPollDue(channelId, now))
                continue; // Not due at this channel's own cadence — see CadenceFor.

            // Cooling after a provider rate-limited this channel (IMusicProvider.TryGetCoolingUntil): skip
            // the call outright, and skip the recovery handover too — both would just draw another call the
            // provider is already refusing. Deliberately NOT routed through ProcessChannelStateAsync/
            // RecordFailure: a 429 is not "nothing playing" (must never publish IsPlaying=false) and not a
            // provider error to back off on top of (the provider's own Retry-After already IS the backoff).
            // _lastPolledAt is intentionally left untouched so polling resumes at the channel's normal
            // cadence — not a fresh full cadence wait — the instant the cooldown clears.
            if (
                providers.Any(p =>
                    p.TryGetCoolingUntil(channelId, out DateTimeOffset until) && until > now
                )
            )
                continue;

            _lastPolledAt[channelId] = now;

            try
            {
                NowPlaying? nowPlaying = await musicService.GetNowPlayingAsync(
                    channelId.ToString(),
                    cancellationToken,
                    isBackgroundPoll: true
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

    /// <summary>Whether this channel's own cadence has elapsed since it was last polled.</summary>
    private bool IsPollDue(Guid channelId, DateTimeOffset now)
    {
        TimeSpan cadence = CadenceFor(channelId);
        return cadence <= PollInterval
            || !_lastPolledAt.TryGetValue(channelId, out DateTimeOffset lastPolled)
            || now - lastPolled >= cadence;
    }

    /// <summary>
    /// How often to poll this channel, by how much its answer is worth right now. Full <see cref="PollInterval"/>
    /// speed is reserved for a channel that is BOTH watched — live, or a dashboard music panel / overlay
    /// now-playing widget / Stream Deck song.changed subscriber connected
    /// (<see cref="IChannelRegistry.HasMusicDemand"/>) — AND actually playing something. Watched but silent drops
    /// to <see cref="QuietPollInterval"/>, unwatched to <see cref="IdlePollInterval"/>. A channel the registry has
    /// not seen yet sits at the quiet cadence: fast enough to pick up its first observation promptly, without an
    /// unknown channel holding a full 1s-per-tick share of the app-wide Spotify budget indefinitely.
    /// </summary>
    private TimeSpan CadenceFor(Guid channelId)
    {
        ChannelContext? ctx = _channelRegistry.Get(channelId);
        if (ctx is null)
            return QuietPollInterval;
        if (!ctx.IsLive && !ctx.HasMusicDemand)
            return IdlePollInterval;
        return IsActivelyPlaying(channelId) ? PollInterval : QuietPollInterval;
    }

    /// <summary>Whether the last observation for this channel had the player actually playing.</summary>
    private bool IsActivelyPlaying(Guid channelId) =>
        _lastState.TryGetValue(channelId, out ChannelPlaybackSnapshot? last) && last.IsPlaying;

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
                // Status-aware, not just "a token row exists". The legacy Services mirror keeps its
                // AccessToken even after the canonical IntegrationConnection has been flipped to
                // needs_reauth or revoked, so filtering on this table alone re-polls dead credentials once
                // per second forever — the 401 storm, and the 4,704-failure connection still in the poll
                // set. The canonical connection decides who is genuinely still connected.
                && db.IntegrationConnections.Any(c =>
                    c.BroadcasterId == s.BroadcasterId
                    && c.Provider == s.Name
                    && c.DeletedAt == null
                    && c.Status == AuthEnums.IntegrationStatus.Connected
                )
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
        if (nowPlaying is null)
        {
            int consecutiveNulls = _consecutiveNullPolls.AddOrUpdate(
                channelId,
                1,
                (_, count) => count + 1
            );

            // A lone blip: keep showing the last known (playing) state and don't publish anything for
            // it — there's nothing stale to correct here since nothing changed on the overlay's side yet.
            // A channel with no prior observation at all has no "last known playing state" to protect, so
            // its first-ever null still publishes immediately, same as before.
            if (consecutiveNulls < NullConfirmationTicks && _lastState.ContainsKey(channelId))
                return;
        }
        else
        {
            _consecutiveNullPolls.TryRemove(channelId, out _);
        }

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
