// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Integrations.Services;
using NomNomzBot.Application.Music.Services;
using NomNomzBot.Domain.Music.Events;
using NomNomzBot.Domain.Music.Exceptions;
using NomNomzBot.Domain.Music.Interfaces;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Infrastructure.Music;

/// <summary>
/// Orchestrates music playback using the registered IMusicProvider implementations.
/// Maintains a per-channel fair queue for song requests and enforces trust-level limits.
/// Provider selection and per-operation gating run purely on <see cref="IMusicProvider.Provider"/>
/// keys and <see cref="IMusicProvider.Capabilities"/> flags — never provider-name checks
/// (music-sr.md §3.5): a member whose required capability is absent fails closed without
/// touching the provider.
/// </summary>
public sealed class MusicService : IMusicService, ISongRequestHandover
{
    /// <summary>Upper bound on the queue-changed snapshot — overlays render a top-of-queue list, never the full backlog.</summary>
    private const int QueueSnapshotSize = 10;

    private readonly IEnumerable<IMusicProvider> _providers;
    private readonly IApplicationDbContext _db;
    private readonly IEventBus _eventBus;
    private readonly IBlockedTrackService _blockedTracks;
    private readonly ISongRequestQueueStore _queueStore;
    private readonly ISongRequestQueuePersistence _queuePersistence;
    private readonly ILogger<MusicService> _logger;
    private readonly IIntegrationCapabilityStore _capabilities;

    public MusicService(
        IEnumerable<IMusicProvider> providers,
        IApplicationDbContext db,
        IEventBus eventBus,
        IBlockedTrackService blockedTracks,
        ISongRequestQueueStore queueStore,
        ISongRequestQueuePersistence queuePersistence,
        ILogger<MusicService> logger,
        IIntegrationCapabilityStore capabilities
    )
    {
        _providers = providers;
        _db = db;
        _eventBus = eventBus;
        _blockedTracks = blockedTracks;
        _queueStore = queueStore;
        _queuePersistence = queuePersistence;
        _logger = logger;
        _capabilities = capabilities;
    }

    /// <summary>Write-through persistence checkpoint (S001b) — called immediately after every in-memory
    /// fair-queue mutation, before the caller sees success, so a hard kill right after never loses a
    /// mutation the caller was told happened. Always re-reads the store's current in-flight entry
    /// (rather than accepting one as a parameter) so every call site stamps the durable row set
    /// correctly without having to track that state itself (S-SR-INFLIGHT-DURABLE).</summary>
    private Task SyncPersistedQueueAsync(
        string broadcasterId,
        FairQueue<SongRequestEntry> queue,
        CancellationToken cancellationToken
    ) =>
        _queuePersistence.SyncAsync(
            broadcasterId,
            queue.GetSnapshot(),
            cancellationToken,
            _queueStore.GetInFlight(broadcasterId)
        );

    public async Task<IReadOnlyList<MusicTrack>> SearchAsync(
        string broadcasterId,
        string query,
        int maxResults = 5,
        CancellationToken cancellationToken = default
    )
    {
        if (!Guid.TryParse(broadcasterId, out Guid tenantId))
            return [];

        IMusicProvider? provider = await GetActiveProviderAsync(tenantId, cancellationToken);
        if (provider is null)
            return [];

        (IReadOnlyList<TrackInfo> results, MusicProviderFailureReason failure) =
            await provider.SearchAsync(tenantId, query, maxResults, cancellationToken);
        if (failure != MusicProviderFailureReason.None)
        {
            _logger.LogWarning(
                "Music search failed for channel {TenantId} via {Provider}: {Failure}",
                tenantId,
                provider.Provider,
                failure
            );
            return [];
        }

        return
        [
            .. results.Select(t => new MusicTrack(
                t.TrackUri,
                t.TrackName,
                t.Artist,
                t.Album,
                t.AlbumArtUrl,
                t.DurationMs,
                t.Provider
            )),
        ];
    }

    public async Task<Result> PlayAsync(
        string broadcasterId,
        CancellationToken cancellationToken = default
    )
    {
        if (!Guid.TryParse(broadcasterId, out Guid tenantId))
            return InvalidChannelId();

        IMusicProvider? provider = await GetActiveProviderAsync(tenantId, cancellationToken);
        if (provider is null)
            return NoProvider();
        if (!HasCapability(provider, MusicProviderCapabilities.PlaybackControl))
            return Unsupported("playback control");

        try
        {
            await provider.PlayAsync(tenantId, cancellationToken);
        }
        catch (PremiumRequiredException ex)
        {
            return PremiumRequired(ex);
        }

        await PublishPlaybackStateChangedAsync(tenantId, provider, cancellationToken);
        return Result.Success();
    }

    public async Task<Result> PauseAsync(
        string broadcasterId,
        CancellationToken cancellationToken = default
    )
    {
        if (!Guid.TryParse(broadcasterId, out Guid tenantId))
            return InvalidChannelId();

        IMusicProvider? provider = await GetActiveProviderAsync(tenantId, cancellationToken);
        if (provider is null)
            return NoProvider();
        if (!HasCapability(provider, MusicProviderCapabilities.PlaybackControl))
            return Unsupported("playback control");

        try
        {
            await provider.PauseAsync(tenantId, cancellationToken);
        }
        catch (PremiumRequiredException ex)
        {
            return PremiumRequired(ex);
        }

        await PublishPlaybackStateChangedAsync(tenantId, provider, cancellationToken);
        return Result.Success();
    }

    public async Task<Result> SkipAsync(
        string broadcasterId,
        CancellationToken cancellationToken = default
    )
    {
        if (!Guid.TryParse(broadcasterId, out Guid tenantId))
            return InvalidChannelId();

        IMusicProvider? provider = await GetActiveProviderAsync(tenantId, cancellationToken);
        if (provider is null)
            return NoProvider();
        if (!HasCapability(provider, MusicProviderCapabilities.Skip))
            return Unsupported("skipping");

        // Every queued request is already sitting in the provider's own queue (see EnqueueResolvedAsync),
        // so a skip only tells the provider to advance — re-pushing the next entry here would queue it a
        // second time. Our own entry for the track that starts playing is dropped by
        // SongRequestQueueReconciler off the live playback state, the same way a natural track end is.
        FairQueue<SongRequestEntry>? fairQueue = _queueStore.TryGet(broadcasterId);
        bool hadPending = fairQueue is not null && !fairQueue.IsEmpty;

        try
        {
            await provider.SkipAsync(tenantId, cancellationToken);
        }
        catch (PremiumRequiredException ex)
        {
            return PremiumRequired(ex);
        }

        // The head of the queue is about to become the playing track — push the fresh snapshot to the
        // sr_queue overlay surfaces.
        if (hadPending)
            await PublishQueueChangedAsync(tenantId, broadcasterId, cancellationToken);

        await PublishPlaybackStateChangedAsync(tenantId, provider, cancellationToken);
        return Result.Success();
    }

    public async Task<Result> PreviousAsync(
        string broadcasterId,
        CancellationToken cancellationToken = default
    )
    {
        if (!Guid.TryParse(broadcasterId, out Guid tenantId))
            return InvalidChannelId();

        IMusicProvider? provider = await GetActiveProviderAsync(tenantId, cancellationToken);
        if (provider is null)
            return NoProvider();
        if (!HasCapability(provider, MusicProviderCapabilities.Previous))
            return Unsupported("previous-track");

        try
        {
            await provider.PreviousAsync(tenantId, cancellationToken);
        }
        catch (PremiumRequiredException ex)
        {
            return PremiumRequired(ex);
        }

        await PublishPlaybackStateChangedAsync(tenantId, provider, cancellationToken);
        return Result.Success();
    }

    public async Task<MusicQueue> GetQueueAsync(
        string broadcasterId,
        CancellationToken cancellationToken = default
    )
    {
        NowPlaying? nowPlaying = await GetNowPlayingAsync(broadcasterId, cancellationToken);

        FairQueue<SongRequestEntry>? queue = _queueStore.TryGet(broadcasterId);

        IReadOnlyList<MusicQueueItem> items = queue is null
            ? []
            : queue
                .GetSnapshot()
                .Select(e => new MusicQueueItem(
                    e.Item.TrackName,
                    e.Item.Artist,
                    e.Item.ImageUrl,
                    e.Item.DurationMs,
                    e.Item.RequestedBy
                ))
                .ToList();

        return new(nowPlaying, items);
    }

    public async Task<Result> AddToQueueAsync(
        string broadcasterId,
        string trackUri,
        string? requestedBy = null,
        CancellationToken cancellationToken = default
    )
    {
        if (!Guid.TryParse(broadcasterId, out Guid tenantId))
            return InvalidChannelId();

        IMusicProvider? provider = await GetActiveProviderAsync(tenantId, cancellationToken);
        if (provider is null)
            return NoProvider();

        // Resolve the request to a concrete track: authoritative id/URI lookup first (trackUri is normally
        // an exact provider URI already), else the provider's best search hit (callers may pass a raw
        // query), else a display-only synthetic entry. Unlike RequestTrackAsync (the free-text !sr entry
        // point, where "provider down" vs "genuinely no such song" changes what the viewer is told),
        // AddToQueueAsync's caller already trusts trackUri as a real, admissible id/URI — the resolve here
        // is best-effort metadata enrichment only, so a lookup failure degrades to the synthetic entry
        // rather than refusing an otherwise-valid admission; a resolve failure is still logged so the
        // metadata gap is diagnosable.
        (TrackInfo? resolvedTrack, MusicProviderFailureReason resolveFailure) =
            await ResolveOrSearchAsync(provider, tenantId, trackUri, cancellationToken);
        if (resolveFailure != MusicProviderFailureReason.None)
            _logger.LogWarning(
                "Track metadata lookup failed for channel {TenantId} via {Provider}: {Failure} — queuing \"{TrackUri}\" with placeholder metadata",
                tenantId,
                provider.Provider,
                resolveFailure,
                trackUri
            );

        TrackInfo trackInfo =
            resolvedTrack
            ?? new TrackInfo
            {
                TrackName = trackUri,
                Artist = "Unknown",
                Album = string.Empty,
                TrackUri = trackUri,
                Provider = "unknown",
            };

        return await EnqueueResolvedAsync(
            tenantId,
            broadcasterId,
            provider,
            trackInfo,
            requestedBy,
            cancellationToken
        );
    }

    public async Task<Result<MusicTrack>> RequestTrackAsync(
        string broadcasterId,
        string query,
        string? requestedBy = null,
        CancellationToken cancellationToken = default
    )
    {
        if (!Guid.TryParse(broadcasterId, out Guid tenantId))
            return InvalidChannelId<MusicTrack>();

        IMusicProvider? provider = await GetActiveProviderAsync(tenantId, cancellationToken);
        if (provider is null)
            return NoProvider<MusicTrack>();

        (TrackInfo? trackInfo, MusicProviderFailureReason failure) = await ResolveOrSearchAsync(
            provider,
            tenantId,
            query,
            cancellationToken
        );
        if (failure != MusicProviderFailureReason.None)
        {
            _logger.LogWarning(
                "Song request search/resolve failed for channel {TenantId} via {Provider}: {Failure} (query: \"{Query}\")",
                tenantId,
                provider.Provider,
                failure,
                query
            );
            return ProviderFailureResult<MusicTrack>(failure);
        }
        if (trackInfo is null)
            return Result.Failure<MusicTrack>($"No tracks found for \"{query}\".", "NOT_FOUND");

        Result enqueued = await EnqueueResolvedAsync(
            tenantId,
            broadcasterId,
            provider,
            trackInfo,
            requestedBy,
            cancellationToken
        );
        if (enqueued.IsFailure)
            return Result.Failure<MusicTrack>(
                enqueued.ErrorMessage!,
                enqueued.ErrorCode,
                enqueued.ErrorDetail
            );

        return Result.Success(
            new MusicTrack(
                trackInfo.TrackUri,
                trackInfo.TrackName,
                trackInfo.Artist,
                trackInfo.Album,
                trackInfo.AlbumArtUrl,
                trackInfo.DurationMs,
                trackInfo.Provider
            )
        );
    }

    /// <summary>
    /// Authoritative link/id resolve first — a pasted Spotify/YouTube track link only ever finds its track
    /// this way (the providers' text search does not parse URLs). A plain search phrase fails the resolve
    /// cheaply (no network call for input that isn't a link/id shape — see each provider's
    /// ExtractId/ExtractVideoId) and falls through to search. A null track with
    /// <see cref="MusicProviderFailureReason.None"/> means neither genuinely found anything; any other
    /// failure reason means the search/resolve never meaningfully ran and must NOT be read as "not found"
    /// by the caller — the bot must never report a provider outage as a song that doesn't exist.
    /// </summary>
    private static async Task<(
        TrackInfo? Track,
        MusicProviderFailureReason Failure
    )> ResolveOrSearchAsync(
        IMusicProvider provider,
        Guid tenantId,
        string queryOrUri,
        CancellationToken cancellationToken
    )
    {
        (TrackInfo? resolved, MusicProviderFailureReason resolveFailure) =
            await provider.ResolveTrackAsync(tenantId, queryOrUri, cancellationToken);
        if (resolveFailure != MusicProviderFailureReason.None)
            return (null, resolveFailure);
        if (resolved is not null)
            return (resolved, MusicProviderFailureReason.None);

        (IReadOnlyList<TrackInfo> hits, MusicProviderFailureReason searchFailure) =
            await provider.SearchAsync(tenantId, queryOrUri, 1, cancellationToken);
        return searchFailure != MusicProviderFailureReason.None
            ? (null, searchFailure)
            : (hits.FirstOrDefault(), MusicProviderFailureReason.None);
    }

    /// <summary>
    /// Translates a provider's <see cref="MusicProviderFailureReason"/> into the caller-facing error code
    /// every SR entry point (the !sr builtin, the reward-pipeline action, the public SR page, scripts)
    /// already switches on — <c>MISSING_SCOPE</c> so the message tells the broadcaster/mod the connection
    /// needs attention, <c>PROVIDER_UNAVAILABLE</c> (distinct from this service's own
    /// <c>SERVICE_UNAVAILABLE</c> "no provider configured at all") so a transient outage never reads as
    /// "go connect Spotify" when it already IS connected.
    /// </summary>
    private static Result<T> ProviderFailureResult<T>(MusicProviderFailureReason failure) =>
        failure switch
        {
            MusicProviderFailureReason.NotConnected => Result.Failure<T>(
                "The music connection needs to be reconnected.",
                "MISSING_SCOPE"
            ),
            _ => Result.Failure<T>(
                "The music provider is temporarily unavailable.",
                "PROVIDER_UNAVAILABLE"
            ),
        };

    /// <summary>
    /// The shared admission path once a track has already been resolved: blocklist gate, fair-queue
    /// enqueue, provider-side push when the queue was empty, and the SongRequested + queue-changed events.
    /// Both <see cref="AddToQueueAsync"/> (caller already has a URI) and <see cref="RequestTrackAsync"/>
    /// (caller has a link/search query) resolve to a <see cref="TrackInfo"/> and land here.
    /// </summary>
    private async Task<Result> EnqueueResolvedAsync(
        Guid tenantId,
        string broadcasterId,
        IMusicProvider provider,
        TrackInfo trackInfo,
        string? requestedBy,
        CancellationToken cancellationToken
    )
    {
        string trackUri = trackInfo.TrackUri;

        // Blocklist admission gate (legacy !bansong): refused before the fair queue ever sees it.
        if (await _blockedTracks.IsBlockedAsync(tenantId, trackUri, cancellationToken))
            return Result.Failure(
                $"\"{trackInfo.TrackName}\" is blocked in this channel.",
                "TRACK_BLOCKED"
            );

        FairQueue<SongRequestEntry> queue = _queueStore.GetOrCreate(broadcasterId);

        // Duplicate gate (legacy parity): the same track already pending, or playing right now, is
        // refused with the requester's name rather than queued twice.
        Result? duplicate = await CheckDuplicateAsync(
            tenantId,
            provider,
            queue,
            trackInfo,
            cancellationToken
        );
        if (duplicate is not null)
            return duplicate;

        SongRequestEntry entry = new(
            trackUri,
            trackInfo.TrackName,
            trackInfo.Artist,
            trackInfo.AlbumArtUrl,
            trackInfo.DurationMs,
            requestedBy ?? "anonymous"
        );

        // Add to fair queue — via the singleton store, so this entry is visible to every later
        // DI scope (next chat command, next dashboard request), not just this one.
        //
        // ATOMIC re-check. CheckDuplicateAsync above already refused the obvious case, but it ran OUTSIDE
        // the queue's lock and it awaits a provider probe in the middle — so two requests for the same
        // track can both clear it and both insert. That is not hypothetical: on 2026-08-25 two API
        // instances were briefly live, every chat command was handled twice, and the queue reached 2,644
        // rows for five distinct tracks. This is the last gate before insertion and it decides under the
        // same lock, so a duplicate cannot slip through however the caller got here.
        bool inserted = queue.TryEnqueueUnique(
            requestedBy ?? "anonymous",
            entry,
            queued => string.Equals(queued.TrackUri, trackUri, StringComparison.OrdinalIgnoreCase)
        );
        if (!inserted)
        {
            (SongRequestEntry Item, int Rank, string OwnerKey) alreadyQueued = queue
                .GetSnapshot()
                .FirstOrDefault(e =>
                    string.Equals(e.Item.TrackUri, trackUri, StringComparison.OrdinalIgnoreCase)
                );
            // ErrorDetail carries the ORIGINAL requester as a structured value, so the chat layer can
            // name them in a toned message without parsing it back out of the sentence above.
            return Result.Failure(
                $"\"{trackInfo.TrackName}\" is already in the queue (requested by {alreadyQueued.Item?.RequestedBy ?? "someone"}).",
                "DUPLICATE_TRACK",
                alreadyQueued.Item?.RequestedBy ?? "someone"
            );
        }
        await SyncPersistedQueueAsync(broadcasterId, queue, cancellationToken);

        // ONE track at a time reaches the provider. The fair queue is the authority on order, and it can
        // only stay that way while the tracks behind the current one are still OURS to re-rank: a viewer's
        // first request must be able to overtake someone's third, which is impossible once both sit in
        // Spotify's own queue in arrival order. So exactly one request is ever handed over — the one now
        // waiting to play — and SongRequestQueueReconciler hands over the next when the provider moves on.
        // A push failure means the request never reached the provider: it is NOT left behind as a phantom
        // entry pretending to be live — that one entry is removed (never the requester's other pending
        // requests) and the caller gets the real, typed reason instead of a false success.
        if (_queueStore.GetInFlight(broadcasterId) is not null)
        {
            // Something of ours is already queued at the provider; this request waits its turn in the
            // fair queue, where a later re-rank can still move it.
            await LogAndAnnounceAsync(
                tenantId,
                broadcasterId,
                trackInfo,
                requestedBy,
                cancellationToken
            );
            return Result.Success();
        }

        try
        {
            bool pushed = await provider.AddToQueueAsync(tenantId, trackUri, cancellationToken);
            if (!pushed)
                return await RollBackAsync(
                    broadcasterId,
                    queue,
                    entry,
                    ProviderErrorOnQueue(trackInfo.TrackName),
                    cancellationToken
                );
        }
        catch (PremiumRequiredException)
        {
            return await RollBackAsync(
                broadcasterId,
                queue,
                entry,
                PremiumRequiredOnQueue(trackInfo.TrackName),
                cancellationToken
            );
        }
        catch (NoActiveDeviceException)
        {
            return await RollBackAsync(
                broadcasterId,
                queue,
                entry,
                NoActiveDeviceOnQueue(trackInfo.TrackName),
                cancellationToken
            );
        }
        catch (MusicAuthenticationFailedException)
        {
            return await RollBackAsync(
                broadcasterId,
                queue,
                entry,
                AuthFailedOnQueue(trackInfo.TrackName),
                cancellationToken
            );
        }
        catch (MusicForbiddenException)
        {
            return await RollBackAsync(
                broadcasterId,
                queue,
                entry,
                ForbiddenOnQueue(trackInfo.TrackName),
                cancellationToken
            );
        }

        // This request is the one now waiting at the provider — remember it so the next request queues
        // behind it in OUR queue instead of being handed over too.
        _queueStore.SetInFlight(broadcasterId, entry);
        // Re-sync so the persisted row set carries the in-flight flag too (S-SR-INFLIGHT-DURABLE) — the
        // sync above (before this request's hand-off outcome was known) could not have stamped it yet.
        await SyncPersistedQueueAsync(broadcasterId, queue, cancellationToken);

        await LogAndAnnounceAsync(
            tenantId,
            broadcasterId,
            trackInfo,
            requestedBy,
            cancellationToken
        );
        return Result.Success();
    }

    /// <summary>The accepted-request side effects, identical whether the request went straight to the
    /// provider or is waiting its turn: the log line, the analytics fact, and the fresh queue snapshot the
    /// dashboard and sr_queue overlay render.</summary>
    private async Task LogAndAnnounceAsync(
        Guid tenantId,
        string broadcasterId,
        TrackInfo trackInfo,
        string? requestedBy,
        CancellationToken cancellationToken
    )
    {
        _logger.LogInformation(
            "Queued track '{Track}' for {BroadcasterId} (requested by {RequestedBy})",
            trackInfo.TrackName,
            broadcasterId,
            requestedBy
        );

        // The accepted-request fact the analytics fold (SongRequests) and future SR surfaces consume. The
        // requester key is what the fair queue records today; the SR engine spec extends this event with the
        // resolved viewer identity when it lands (music-sr.md §2).
        await _eventBus.PublishAsync(
            new SongRequestedEvent
            {
                BroadcasterId = tenantId,
                UserId = requestedBy ?? "anonymous",
                UserDisplayName = requestedBy ?? "anonymous",
                TrackUri = trackInfo.TrackUri,
                TrackName = trackInfo.TrackName,
            },
            cancellationToken
        );

        await PublishQueueChangedAsync(tenantId, broadcasterId, cancellationToken);
    }

    public async Task<Result> SetVolumeAsync(
        string broadcasterId,
        int volume,
        CancellationToken cancellationToken = default
    )
    {
        if (!Guid.TryParse(broadcasterId, out Guid tenantId))
            return InvalidChannelId();

        if (volume is < 0 or > 100)
            return Result.Failure("Volume must be between 0 and 100.", "VALIDATION_FAILED");

        IMusicProvider? provider = await GetActiveProviderAsync(tenantId, cancellationToken);
        if (provider is null)
            return NoProvider();
        if (!HasCapability(provider, MusicProviderCapabilities.Volume))
            return Unsupported("volume control");

        try
        {
            await provider.SetVolumeAsync(tenantId, volume, cancellationToken);
        }
        catch (PremiumRequiredException ex)
        {
            return PremiumRequired(ex);
        }

        return Result.Success();
    }

    public async Task<NowPlaying?> GetNowPlayingAsync(
        string broadcasterId,
        CancellationToken cancellationToken = default
    )
    {
        if (!Guid.TryParse(broadcasterId, out Guid tenantId))
            return null;

        IMusicProvider? provider = await GetActiveProviderAsync(tenantId, cancellationToken);
        if (provider is null || !HasCapability(provider, MusicProviderCapabilities.NowPlaying))
            return null;

        TrackInfo? track = await provider.GetCurrentTrackAsync(tenantId, cancellationToken);
        if (track is null)
            return null;

        return new(
            track.TrackName,
            track.Artist,
            track.Album,
            track.AlbumArtUrl,
            track.DurationMs,
            track.ProgressMs,
            track.IsPlaying,
            // The real device volume when the provider reports one; 100 (full) is a documented fallback for
            // providers/states that don't — never a stand-in for "we don't actually know".
            track.VolumePercent
                ?? 100,
            null,
            track.Provider,
            track.TrackUri,
            track.ShuffleEnabled,
            track.RepeatMode,
            track.ArtistId,
            track.CanSetShuffle,
            track.CanSetRepeat,
            track.CanSkipNext,
            track.CanSkipPrevious,
            track.CanSeek,
            track.CanPause,
            track.CanResume
        );
    }

    public async Task<string?> GetActiveProviderKeyAsync(
        string broadcasterId,
        CancellationToken cancellationToken = default
    )
    {
        if (!Guid.TryParse(broadcasterId, out Guid tenantId))
            return null;

        IMusicProvider? provider = await GetActiveProviderAsync(tenantId, cancellationToken);
        return provider?.Provider;
    }

    public async Task<string?> GetActiveProviderAuthStatusAsync(
        string broadcasterId,
        CancellationToken cancellationToken = default
    )
    {
        if (!Guid.TryParse(broadcasterId, out Guid tenantId))
            return null;

        IMusicProvider? provider = await GetActiveProviderAsync(tenantId, cancellationToken);
        if (provider is null)
            return null;

        IReadOnlyDictionary<string, bool> observed = _capabilities.GetObserved(
            tenantId,
            provider.Provider
        );

        // Forbidden takes precedence when somehow both are observed true (shouldn't happen — a live
        // call is classified as exactly one of the two — but forbidden is the more specific reason).
        if (observed.GetValueOrDefault(SpotifyMusicProvider.ForbiddenCapabilityKey))
            return "forbidden";
        if (observed.GetValueOrDefault(SpotifyMusicProvider.NeedsReauthCapabilityKey))
            return "needs_reauth";

        return null;
    }

    // ─── Trust-level enforcement ──────────────────────────────────────────────

    /// <summary>
    /// Validates that a user's trust tier permits queuing music.
    /// Returns null if allowed, or an error message if blocked.
    /// </summary>
    public string? CheckTrustPermission(double trustScore, bool isYouTubeContent)
    {
        TrustTier tier = TrustScoreCalculator.GetTier(trustScore);

        return tier switch
        {
            TrustTier.Untrusted =>
                "Your trust score is too low. Requests require moderator approval.",
            TrustTier.Low when isYouTubeContent =>
                "YouTube requests are not available at your trust level. Try Spotify.",
            _ => null,
        };
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Resolves the channel's active provider: the connected-integration names for the tenant,
    /// intersected with the registered provider keys, preferring a provider that can drive playback
    /// (interim priority rule until the §3.1 ProviderPriority config lands; keeps today's
    /// Spotify-before-YouTube ordering without naming either).
    /// </summary>
    private async Task<IMusicProvider?> GetActiveProviderAsync(
        Guid tenantId,
        CancellationToken cancellationToken
    )
    {
        // Look up which services are connected for this broadcaster
        List<string> connected = await _db
            .Services.Where(s => s.BroadcasterId == tenantId && s.Enabled && s.AccessToken != null)
            .Select(s => s.Name)
            .ToListAsync(cancellationToken);

        IMusicProvider? provider = _providers
            .Where(p => connected.Contains(p.Provider))
            .OrderByDescending(p => HasCapability(p, MusicProviderCapabilities.PlaybackControl))
            .ThenBy(p => p.Provider, StringComparer.Ordinal)
            .FirstOrDefault();

        if (provider is null)
            _logger.LogDebug("No active music provider for broadcaster {BroadcasterId}", tenantId);

        return provider;
    }

    private static bool HasCapability(
        IMusicProvider provider,
        MusicProviderCapabilities capability
    ) => (provider.Capabilities & capability) == capability;

    private static Result InvalidChannelId() =>
        Result.Failure("Invalid channel id.", "VALIDATION_FAILED");

    private static Result<T> InvalidChannelId<T>() =>
        Result.Failure<T>("Invalid channel id.", "VALIDATION_FAILED");

    private static Result NoProvider() =>
        Result.Failure("No active music provider.", "SERVICE_UNAVAILABLE");

    private static Result<T> NoProvider<T>() =>
        Result.Failure<T>("No active music provider.", "SERVICE_UNAVAILABLE");

    private static Result Unsupported(string operation) =>
        Result.Failure(
            $"The active music provider does not support {operation}.",
            "CAPABILITY_UNSUPPORTED"
        );

    private static Result<T> Unsupported<T>(string operation) =>
        Result.Failure<T>(
            $"The active music provider does not support {operation}.",
            "CAPABILITY_UNSUPPORTED"
        );

    private static Result PremiumRequired(PremiumRequiredException ex) =>
        Result.Failure(ex.Message, "PREMIUM_REQUIRED");

    // ── Queue-admission failure replies (!sr / AddToQueueAsync) ────────────────
    // Each carries the track name, since the viewer just asked for that specific song and the reply
    // needs to make clear which request failed and whether trying again is worth it.

    private static Result NoActiveDeviceOnQueue(string trackName) =>
        Result.Failure(
            $"Couldn't queue \"{trackName}\" — nothing is playing on any device right now. Start playback and try again.",
            "NO_ACTIVE_DEVICE"
        );

    private static Result AuthFailedOnQueue(string trackName) =>
        Result.Failure(
            $"Couldn't queue \"{trackName}\" — the music connection needs to be reconnected.",
            "MUSIC_AUTH_FAILED"
        );

    private static Result ForbiddenOnQueue(string trackName) =>
        Result.Failure(
            $"Couldn't queue \"{trackName}\" — the music connection doesn't have permission for that.",
            "MUSIC_FORBIDDEN"
        );

    private static Result PremiumRequiredOnQueue(string trackName) =>
        Result.Failure(
            $"Couldn't queue \"{trackName}\" — a Premium account is required for that.",
            "PREMIUM_REQUIRED"
        );

    private static Result ProviderErrorOnQueue(string trackName) =>
        Result.Failure(
            $"Couldn't queue \"{trackName}\" — the music service had a problem. Try again in a moment.",
            "PROVIDER_ERROR"
        );

    public async Task<bool> RemoveFromQueueAsync(
        string broadcasterId,
        int position,
        CancellationToken cancellationToken = default
    )
    {
        FairQueue<SongRequestEntry>? queue = _queueStore.TryGet(broadcasterId);
        bool removed = queue is not null && queue.RemoveAt(position);

        if (removed)
        {
            await SyncPersistedQueueAsync(broadcasterId, queue!, cancellationToken);
            if (Guid.TryParse(broadcasterId, out Guid tenantId))
                await PublishQueueChangedAsync(tenantId, broadcasterId, cancellationToken);
        }

        return removed;
    }

    // ── Remote controls (capability-gated §3.5 members) ─────────────────────────

    public async Task<Result> SeekAsync(
        string broadcasterId,
        int positionMs,
        CancellationToken cancellationToken = default
    )
    {
        if (!Guid.TryParse(broadcasterId, out Guid tenantId))
            return InvalidChannelId();

        if (positionMs < 0)
            return Result.Failure("Seek position cannot be negative.", "VALIDATION_FAILED");

        IMusicProvider? provider = await GetActiveProviderAsync(tenantId, cancellationToken);
        if (provider is null)
            return NoProvider();
        if (!HasCapability(provider, MusicProviderCapabilities.Seek))
            return Unsupported("seeking");

        try
        {
            // The §3.5 seam speaks whole seconds; the legacy wire contract still carries milliseconds.
            await provider.SeekAsync(tenantId, positionMs / 1000, cancellationToken);
        }
        catch (PremiumRequiredException ex)
        {
            return PremiumRequired(ex);
        }

        return Result.Success();
    }

    public async Task<Result> SetShuffleAsync(
        string broadcasterId,
        bool enabled,
        CancellationToken cancellationToken = default
    )
    {
        if (!Guid.TryParse(broadcasterId, out Guid tenantId))
            return InvalidChannelId();

        IMusicProvider? provider = await GetActiveProviderAsync(tenantId, cancellationToken);
        if (provider is null)
            return NoProvider();
        if (!HasCapability(provider, MusicProviderCapabilities.Shuffle))
            return Unsupported("shuffle");

        try
        {
            await provider.SetShuffleAsync(tenantId, enabled, cancellationToken);
        }
        catch (PremiumRequiredException ex)
        {
            return PremiumRequired(ex);
        }

        return Result.Success();
    }

    public async Task<Result> SetRepeatAsync(
        string broadcasterId,
        string mode,
        CancellationToken cancellationToken = default
    )
    {
        if (!Guid.TryParse(broadcasterId, out Guid tenantId))
            return InvalidChannelId();

        if (!Enum.TryParse(mode, ignoreCase: true, out MusicRepeatMode repeatMode))
            return Result.Failure(
                "Repeat mode must be 'off', 'track', or 'context'.",
                "VALIDATION_FAILED"
            );

        IMusicProvider? provider = await GetActiveProviderAsync(tenantId, cancellationToken);
        if (provider is null)
            return NoProvider();
        if (!HasCapability(provider, MusicProviderCapabilities.Repeat))
            return Unsupported("repeat mode");

        try
        {
            await provider.SetRepeatAsync(tenantId, repeatMode, cancellationToken);
        }
        catch (PremiumRequiredException ex)
        {
            return PremiumRequired(ex);
        }

        return Result.Success();
    }

    public async Task<Result> TransferPlaybackAsync(
        string broadcasterId,
        string deviceId,
        bool play = false,
        CancellationToken cancellationToken = default
    )
    {
        if (!Guid.TryParse(broadcasterId, out Guid tenantId))
            return InvalidChannelId();

        IMusicProvider? provider = await GetActiveProviderAsync(tenantId, cancellationToken);
        if (provider is null)
            return NoProvider();
        if (!HasCapability(provider, MusicProviderCapabilities.TransferDevice))
            return Unsupported("device transfer");

        try
        {
            await provider.TransferPlaybackAsync(tenantId, deviceId, play, cancellationToken);
        }
        catch (PremiumRequiredException ex)
        {
            return PremiumRequired(ex);
        }
        catch (DeviceTransferFailedException ex)
        {
            return Result.Failure(ex.Message, "DEVICE_TRANSFER_FAILED");
        }

        return Result.Success();
    }

    public async Task<IReadOnlyList<MusicDeviceDto>> GetDevicesAsync(
        string broadcasterId,
        CancellationToken cancellationToken = default
    )
    {
        if (!Guid.TryParse(broadcasterId, out Guid tenantId))
            return [];

        IMusicProvider? provider = await GetActiveProviderAsync(tenantId, cancellationToken);
        if (provider is null || !HasCapability(provider, MusicProviderCapabilities.TransferDevice))
            return [];

        IReadOnlyList<MusicDeviceInfo> devices = await provider.GetDevicesAsync(
            tenantId,
            cancellationToken
        );
        return devices
            .Select(d => new MusicDeviceDto(d.Id, d.Name, d.Type, d.IsActive, d.VolumePercent ?? 0))
            .ToList()
            .AsReadOnly();
    }

    public async Task<IReadOnlyList<MusicPlaylistDto>> GetPlaylistsAsync(
        string broadcasterId,
        int offset = 0,
        int limit = 20,
        CancellationToken cancellationToken = default
    )
    {
        if (!Guid.TryParse(broadcasterId, out Guid tenantId))
            return [];

        IMusicProvider? provider = await GetActiveProviderAsync(tenantId, cancellationToken);
        if (
            provider is null
            || !HasCapability(provider, MusicProviderCapabilities.Playlists)
            || provider is not IMusicRemoteProvider remote
        )
            return [];

        IReadOnlyList<MusicPlaylist> playlists = await remote.GetPlaylistsAsync(
            tenantId,
            offset,
            limit,
            cancellationToken
        );
        return playlists
            .Select(p => new MusicPlaylistDto(p.Id, p.Name, p.Uri, p.TrackCount, p.ImageUrl))
            .ToList()
            .AsReadOnly();
    }

    public async Task<Result<string>> GetEmbeddedPlaybackTokenAsync(
        string broadcasterId,
        CancellationToken cancellationToken = default
    )
    {
        if (!Guid.TryParse(broadcasterId, out Guid tenantId))
            return InvalidChannelId<string>();

        IMusicProvider? provider = await GetActiveProviderAsync(tenantId, cancellationToken);
        if (provider is null)
            return NoProvider<string>();
        if (!HasCapability(provider, MusicProviderCapabilities.EmbeddedPlayback))
            return Unsupported<string>("embedded playback");

        string? token = await provider.GetEmbeddedPlaybackTokenAsync(tenantId, cancellationToken);
        return token is null
            ? Result.Failure<string>(
                "This channel's Spotify connection hasn't granted the streaming scope yet — reconnect Spotify to enable it.",
                "MISSING_SCOPE"
            )
            : Result.Success(token);
    }

    public async Task<bool> PlayContextAsync(
        string broadcasterId,
        string contextUri,
        CancellationToken cancellationToken = default
    )
    {
        if (!Guid.TryParse(broadcasterId, out Guid tenantId))
            return false;

        IMusicProvider? provider = await GetActiveProviderAsync(tenantId, cancellationToken);
        if (
            provider is null
            || !HasCapability(provider, MusicProviderCapabilities.PlaybackControl)
            || provider is not IMusicRemoteProvider remote
        )
            return false;

        try
        {
            await remote.PlayContextAsync(tenantId, contextUri, cancellationToken);
        }
        catch (PremiumRequiredException)
        {
            return false;
        }

        await PublishPlaybackStateChangedAsync(tenantId, provider, cancellationToken);
        return true;
    }

    /// <summary>
    /// Publishes <see cref="PlaybackStateChangedEvent"/> right after a successful mutation (play/pause/skip/
    /// play-context) so the dashboard + overlay update instantly instead of waiting for the next
    /// <c>MusicStatePollingService</c> tick. Re-reads the provider's current track rather than guessing the new
    /// state, since e.g. a skip's next track is only known to the provider.
    /// </summary>
    private async Task PublishPlaybackStateChangedAsync(
        Guid tenantId,
        IMusicProvider provider,
        CancellationToken cancellationToken
    )
    {
        TrackInfo? track = await provider.GetCurrentTrackAsync(tenantId, cancellationToken);

        await _eventBus.PublishAsync(
            new PlaybackStateChangedEvent
            {
                BroadcasterId = tenantId,
                IsPlaying = track?.IsPlaying ?? false,
                TrackName = track?.TrackName,
                Artist = track?.Artist,
                Album = track?.Album,
                AlbumArtUrl = track?.AlbumArtUrl,
                DurationMs = track?.DurationMs ?? 0,
                ProgressMs = track?.ProgressMs ?? 0,
                Provider = track?.Provider,
                TrackUri = track?.TrackUri,
                ArtistId = track?.ArtistId,
                ShuffleEnabled = track?.ShuffleEnabled ?? false,
                RepeatMode = track?.RepeatMode ?? MusicRepeatMode.Off,
                VolumePercent = track?.VolumePercent ?? 100,
                ObservedAt = DateTimeOffset.UtcNow,
                CanSetShuffle = track?.CanSetShuffle ?? true,
                CanSetRepeat = track?.CanSetRepeat ?? true,
                CanSkipNext = track?.CanSkipNext ?? true,
                CanSkipPrevious = track?.CanSkipPrevious ?? true,
                CanSeek = track?.CanSeek ?? true,
                CanPause = track?.CanPause ?? true,
                CanResume = track?.CanResume ?? true,
            },
            cancellationToken
        );
    }

    /// <summary>
    /// Publishes <see cref="SongRequestQueueChangedEvent"/> with the fresh top-of-queue snapshot right after any
    /// fair-queue mutation (add / skip-dequeue / remove), so the standing <c>sr_queue</c> overlay widget re-renders
    /// from the event alone instead of polling.
    /// </summary>
    private Task PublishQueueChangedAsync(
        Guid tenantId,
        string broadcasterId,
        CancellationToken cancellationToken
    ) =>
        _eventBus.PublishAsync(
            new SongRequestQueueChangedEvent
            {
                BroadcasterId = tenantId,
                Items = SnapshotQueue(broadcasterId),
            },
            cancellationToken
        );

    private IReadOnlyList<SongRequestQueueSnapshotItem> SnapshotQueue(string broadcasterId)
    {
        FairQueue<SongRequestEntry>? queue = _queueStore.TryGet(broadcasterId);
        return queue is null
            ? []
            : queue
                .GetSnapshot()
                .Take(QueueSnapshotSize)
                .Select(e => new SongRequestQueueSnapshotItem(
                    e.Item.TrackName,
                    e.Item.RequestedBy,
                    e.Item.DurationMs / 1000
                ))
                .ToList();
    }

    /// <inheritdoc cref="ISongRequestHandover.HandOverNextAsync"/>
    public async Task HandOverNextAsync(
        string broadcasterId,
        CancellationToken cancellationToken = default
    )
    {
        if (!Guid.TryParse(broadcasterId, out Guid tenantId))
            return;

        // Never two of ours at the provider — that is the invariant the whole fair queue rests on. Callers
        // that fire on a cadence (the playback poller's recovery tick) can therefore call this freely.
        if (_queueStore.GetInFlight(broadcasterId) is not null)
            return;

        FairQueue<SongRequestEntry>? queue = _queueStore.TryGet(broadcasterId);
        SongRequestEntry? next = queue?.Peek();
        if (next is null)
            return;

        IMusicProvider? provider = await GetActiveProviderAsync(tenantId, cancellationToken);
        if (provider is null)
            return;

        try
        {
            if (!await provider.AddToQueueAsync(tenantId, next.TrackUri, cancellationToken))
                return;
        }
        catch (Exception ex)
            when (ex
                    is PremiumRequiredException
                        or NoActiveDeviceException
                        or MusicAuthenticationFailedException
                        or MusicForbiddenException
            )
        {
            // Nothing playable right now (no device, dead token, non-Premium). The request keeps its place
            // at the head of the queue: the next playback tick tries again, and no viewer loses a request
            // because the streamer's player happened to be closed for a moment.
            _logger.LogDebug(
                ex,
                "Could not hand '{Track}' to the provider for {BroadcasterId} — it stays queued.",
                next.TrackName,
                broadcasterId
            );
            return;
        }

        _queueStore.SetInFlight(broadcasterId, next);
        // Persist the in-flight flag immediately — without this a restart right after hand-over forgets
        // that `next` was already pushed to the provider and re-hands it over a second time
        // (S-SR-INFLIGHT-DURABLE).
        await SyncPersistedQueueAsync(broadcasterId, queue!, cancellationToken);
    }

    /// <summary>Takes one rejected entry back out of the fair queue and returns the caller's failure —
    /// only that entry, never the requester's other pending requests.</summary>
    private async Task<Result> RollBackAsync(
        string broadcasterId,
        FairQueue<SongRequestEntry> queue,
        SongRequestEntry entry,
        Result failure,
        CancellationToken cancellationToken
    )
    {
        queue.RemoveFirst(e => ReferenceEquals(e, entry));
        await SyncPersistedQueueAsync(broadcasterId, queue, cancellationToken);
        return failure;
    }

    /// <summary>
    /// Legacy-parity duplicate gate: the same track already waiting in the fair queue, or playing right
    /// now, is refused instead of queued a second time. Returns null when the request is not a duplicate.
    /// </summary>
    private static async Task<Result?> CheckDuplicateAsync(
        Guid tenantId,
        IMusicProvider provider,
        FairQueue<SongRequestEntry> queue,
        TrackInfo trackInfo,
        CancellationToken cancellationToken
    )
    {
        (SongRequestEntry Item, int Rank, string OwnerKey) pending = queue
            .GetSnapshot()
            .FirstOrDefault(e =>
                string.Equals(
                    e.Item.TrackUri,
                    trackInfo.TrackUri,
                    StringComparison.OrdinalIgnoreCase
                )
            );
        if (pending.Item is not null)
            return Result.Failure(
                $"\"{trackInfo.TrackName}\" is already in the queue (requested by {pending.Item.RequestedBy}).",
                "DUPLICATE_TRACK"
            );

        // The probe is best-effort by contract: IMusicProvider.GetCurrentTrackAsync returns null for
        // every "cannot answer" (no device, dead token, provider error), and a null read must never be
        // read as a match — an unanswerable probe leaves the verdict to the real admission push below.
        TrackInfo? current = await provider.GetCurrentTrackAsync(tenantId, cancellationToken);

        if (
            current?.TrackUri is not null
            && string.Equals(
                current.TrackUri,
                trackInfo.TrackUri,
                StringComparison.OrdinalIgnoreCase
            )
        )
            return Result.Failure(
                $"\"{trackInfo.TrackName}\" is playing right now.",
                "DUPLICATE_TRACK"
            );

        return null;
    }
}
