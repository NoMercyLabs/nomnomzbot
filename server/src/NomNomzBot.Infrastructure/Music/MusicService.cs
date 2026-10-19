// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Security;
using NomNomzBot.Application.DTOs.Economy;
using NomNomzBot.Application.Economy.Services;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Application.Integrations.Services;
using NomNomzBot.Application.Music.Dtos;
using NomNomzBot.Application.Music.Services;
using NomNomzBot.Domain.Economy.Enums;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Music.Events;
using NomNomzBot.Domain.Music.Exceptions;
using NomNomzBot.Domain.Music.Interfaces;
using NomNomzBot.Domain.Music.ValueObjects;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Music.YouTubeKeyless;

namespace NomNomzBot.Infrastructure.Music;

/// <summary>
/// Orchestrates music playback using the registered IMusicProvider implementations.
/// Maintains a per-channel fair queue for song requests and enforces trust-level limits.
/// Provider selection and per-operation gating run purely on <see cref="IMusicProvider.Provider"/>
/// keys and <see cref="IMusicProvider.Capabilities"/> flags — never provider-name checks
/// (music-sr.md §3.5): a member whose required capability is absent fails closed without
/// touching the provider.
/// </summary>
public sealed class MusicService : IMusicService
{
    /// <summary>Upper bound on the queue-changed snapshot — overlays render a top-of-queue list, never the full backlog.</summary>
    private const int QueueSnapshotSize = 10;
    private const string RequestsClosedCode = "SR_CLOSED";
    private const string RequestsClosedMessage = "The song queue is closed.";
    private const int DefaultMaxSongSeconds = 360;

    private const string UnsupportedLinkMessage =
        "That link can't be used here. Send a song name, or a link for the music service this channel uses.";

    private readonly IEnumerable<IMusicProvider> _providers;
    private readonly IApplicationDbContext _db;
    private readonly IEventBus _eventBus;
    private readonly IBlockedTrackService _blockedTracks;
    private readonly ISongRequestQueueStore _queueStore;
    private readonly ISongRequestQueuePersistence _queuePersistence;
    private readonly ILogger<MusicService> _logger;
    private readonly IIntegrationCapabilityStore _capabilities;
    private readonly IMusicConfigService _config;
    private readonly ICurrencyAccountService _accounts;
    private readonly INowPlayingCache _nowPlayingCache;
    private readonly IOutboundSanctionAccessor _sanctions;
    private readonly IUserIdentityService _identities;
    private readonly IForeignLinkTitleLookup _linkTitles;
    private readonly RefusedSongRequestStore _refused;
    private readonly IPlaybackInputSink _playbackInput;
    private readonly IPlaybackStateReader _playbackState;

    public MusicService(
        IEnumerable<IMusicProvider> providers,
        IApplicationDbContext db,
        IEventBus eventBus,
        IBlockedTrackService blockedTracks,
        ISongRequestQueueStore queueStore,
        ISongRequestQueuePersistence queuePersistence,
        ILogger<MusicService> logger,
        IIntegrationCapabilityStore capabilities,
        IMusicConfigService config,
        ICurrencyAccountService accounts,
        INowPlayingCache nowPlayingCache,
        IOutboundSanctionAccessor sanctions,
        IUserIdentityService identities,
        IForeignLinkTitleLookup linkTitles,
        IPlaybackInputSink playbackInput,
        IPlaybackStateReader playbackState,
        RefusedSongRequestStore? refused = null
    )
    {
        _playbackInput = playbackInput;
        _playbackState = playbackState;
        _refused = refused ?? new();
        _linkTitles = linkTitles;
        _providers = providers;
        _db = db;
        _eventBus = eventBus;
        _blockedTracks = blockedTracks;
        _config = config;
        _queueStore = queueStore;
        _queuePersistence = queuePersistence;
        _logger = logger;
        _capabilities = capabilities;
        _accounts = accounts;
        _nowPlayingCache = nowPlayingCache;
        _sanctions = sanctions;
        _identities = identities;
    }

    /// <summary>
    /// Refunds a removed/banned queue entry's <see cref="SongRequestEntry.Cost"/> to
    /// <see cref="SongRequestEntry.RequesterUserId"/> (S067b) — the same reversing-ledger-entry pattern
    /// <c>MediaShareService.RefundIfChargedAsync</c> uses. A no-op whenever <see cref="SongRequestEntry.Cost"/>
    /// is 0 (free — every admission today) or the requester never resolved to a viewer account (e.g. an
    /// anonymous public song-request page submission), so this never fabricates a refund for a request
    /// nothing ever charged.
    /// </summary>
    private async Task RefundIfPaidAsync(
        Guid tenantId,
        SongRequestEntry entry,
        CancellationToken cancellationToken
    )
    {
        if (entry.Cost <= 0 || entry.RequesterUserId is not { } requesterUserId)
            return;

        Result<CurrencyLedgerEntryDto> refund = await _accounts.PostLedgerEntryAsync(
            tenantId,
            new(
                requesterUserId,
                entry.Cost,
                nameof(CurrencyEntryType.RefundSongRequest),
                nameof(CurrencyLedgerSourceType.SongRequest),
                SourceId: null,
                EventId: null,
                Reason: $"Song request removed: {entry.TrackName}",
                ActorUserId: null,
                IdempotencyKey: $"song-request-refund:{tenantId}:{requesterUserId}:{entry.TrackUri}:{Guid.CreateVersion7()}"
            ),
            cancellationToken
        );
        if (refund.IsFailure)
            _logger.LogWarning(
                "Song request refund failed for channel {TenantId}, viewer {RequesterUserId}, amount {Cost}: {Error}",
                tenantId,
                requesterUserId,
                entry.Cost,
                refund.ErrorMessage
            );
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

        MusicConfigDto? config = await GetConfigOrNullAsync(broadcasterId, cancellationToken);
        bool categoryOnly =
            config?.YouTubeMusicCategoryOnly ?? MusicConfigDefaults.YouTubeMusicCategoryOnly;

        return
        [
            .. results
                .Where(t => YouTubeMusicCategory.Admits(categoryOnly, t, null))
                .Select(t => new MusicTrack(
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

        // Reachable off a pipeline action (go-live/raid-start auto-resume) or PlayOnceResumeHandler's
        // own react-to-playback-change path — neither carries an HTTP-request sanction of its own.
        using IDisposable sanction = _sanctions.Begin(
            OutboundSanction.ChannelConfiguration("music:playback_control")
        );

        Result control = await SendOwnerControlAsync(
            tenantId,
            provider,
            new OwnerResume(),
            cancellationToken
        );
        if (control.IsFailure)
            return control;

        await PublishPlaybackStateChangedAsync(
            tenantId,
            provider,
            cancellationToken,
            assumeIsPlaying: true
        );
        return Result.Success();
    }

    /// <inheritdoc cref="IMusicService.PlayTrackOnceAsync"/>
    public async Task<Result> PlayTrackOnceAsync(
        string broadcasterId,
        string trackUri,
        CancellationToken cancellationToken = default
    )
    {
        if (!Guid.TryParse(broadcasterId, out Guid tenantId))
            return InvalidChannelId();
        if (string.IsNullOrWhiteSpace(trackUri))
            return Result.Failure("A track is required.", "VALIDATION_FAILED");

        IMusicProvider? provider = await GetActiveProviderAsync(tenantId, cancellationToken);
        if (provider is null)
            return NoProvider();
        if (
            !HasCapability(provider, MusicProviderCapabilities.Queue)
            || !HasCapability(provider, MusicProviderCapabilities.Skip)
        )
            return Unsupported("playing a specific track");

        // Called from a pipeline action (go-live, raid-start) with no person present — the same
        // already-opted-in, no-live-request basis as the other pipeline entry points.
        using IDisposable sanction = _sanctions.Begin(
            OutboundSanction.ChannelConfiguration("music:song_request_dispatch")
        );

        try
        {
            // Push onto the provider's OWN live queue (never our fair queue / SongRequestQueueStore —
            // this is a playback-context primitive, not a song request) then skip straight to it. The
            // provider's context (playlist/album) is never replaced, so whatever plays after this track
            // finishes is exactly what the context would have played next on its own.
            if (provider is IMusicProviderPlayNow playNow)
            {
                // A provider whose player takes "play this now" directly (YouTube's overlay player) keeps
                // its own waiting queue untouched, so a queued request still plays after this track.
                bool playing = await playNow.PlayNowAsync(tenantId, trackUri, cancellationToken);
                if (!playing)
                    return Result.Failure(
                        "Couldn't play the track on the music provider.",
                        "PROVIDER_ERROR"
                    );
            }
            else
            {
                bool pushed = await provider.AddToQueueAsync(tenantId, trackUri, cancellationToken);
                if (!pushed)
                    return Result.Failure(
                        "Couldn't queue the track on the music provider.",
                        "PROVIDER_ERROR"
                    );

                await provider.SkipAsync(tenantId, cancellationToken);
            }
        }
        catch (PremiumRequiredException ex)
        {
            return PremiumRequired(ex);
        }
        catch (NoActiveDeviceException ex)
        {
            return NothingPlaying(ex.Provider);
        }
        catch (MusicAuthenticationFailedException)
        {
            return Result.Failure(
                "The music connection needs to be reconnected.",
                "MUSIC_AUTH_FAILED"
            );
        }
        catch (MusicForbiddenException)
        {
            return Result.Failure(
                "The music connection doesn't have permission for that.",
                "MUSIC_FORBIDDEN"
            );
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

        // PlayOnceResumeHandler calls this off a PlaybackStateChangedEvent the poller published on its
        // own schedule — no person present, no HTTP request behind it — so it carries no ambient
        // sanction of its own to inherit.
        using IDisposable sanction = _sanctions.Begin(
            OutboundSanction.ChannelConfiguration("music:playback_control")
        );

        Result control = await SendOwnerControlAsync(
            tenantId,
            provider,
            new OwnerPause(),
            cancellationToken
        );
        if (control.IsFailure)
            return control;

        await PublishPlaybackStateChangedAsync(
            tenantId,
            provider,
            cancellationToken,
            assumeIsPlaying: false
        );
        return Result.Success();
    }

    /// <summary>
    /// The internal user id for whoever triggered an action. Chat-driven callers hold the platform user id, the
    /// dashboard holds the internal one. Empty when nobody resolves (a timer or an automation token).
    /// </summary>
    private async Task<string> ResolveInternalUserIdAsync(
        string actorId,
        string? actorPlatform,
        CancellationToken cancellationToken
    )
    {
        if (Guid.TryParse(actorId, out Guid internalId))
            return internalId.ToString();
        if (string.IsNullOrWhiteSpace(actorId))
            return string.Empty;

        Result<Guid> resolved = await _identities.ResolveUserAsync(
            actorPlatform ?? AuthEnums.Platform.Twitch,
            actorId,
            getOrCreate: false,
            cancellationToken
        );
        return resolved.IsSuccess ? resolved.Value.ToString() : string.Empty;
    }

    public async Task<Result> SkipAsync(
        string broadcasterId,
        string skippedByUserId,
        string? skippedByPlatform = null,
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

        // Read before the provider advances: afterwards the cache holds the next track.
        NowPlaying? skippedTrack = await GetNowPlayingAsync(broadcasterId, cancellationToken);

        // Reachable off a pipeline action (!songskip / !songwrong / !bansong automation) with no HTTP
        // request of its own — so it opens its own sanction.
        using IDisposable sanction = _sanctions.Begin(
            OutboundSanction.ChannelConfiguration("music:playback_control")
        );

        // The director stops the playing song and starts the next head; the provider's own skip is never
        // called. An engine that refuses leaves the director Blocked, and that reason is the answer.
        await _playbackInput.SendAsync(tenantId, new OwnerSkip(), cancellationToken);

        PlaybackState state = _playbackState.StateOf(tenantId);
        if (state.Kind == PlaybackStateKind.Blocked)
            return BlockedOnSkip(provider, state.Reason);

        if (skippedTrack?.TrackName is { } skippedTrackName)
            await _eventBus.PublishAsync(
                new SongSkippedEvent
                {
                    BroadcasterId = tenantId,
                    SkippedByUserId = await ResolveInternalUserIdAsync(
                        skippedByUserId,
                        skippedByPlatform,
                        cancellationToken
                    ),
                    TrackName = skippedTrackName,
                },
                cancellationToken
            );

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

        // Reachable off a pipeline action with no HTTP request of its own — same no-person-present
        // shape as the other pipeline entry points.
        using IDisposable sanction = _sanctions.Begin(
            OutboundSanction.ChannelConfiguration("music:playback_control")
        );

        Result control = await SendOwnerControlAsync(
            tenantId,
            provider,
            new OwnerPrevious(),
            cancellationToken
        );
        if (control.IsFailure)
            return control;

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

        SongRequestEntry? inFlight = _queueStore.GetInFlight(broadcasterId);
        IReadOnlyList<MusicQueueItem> items = queue is null
            ? []
            : queue
                .GetSnapshot()
                .Select(e => new MusicQueueItem(
                    e.Item.TrackName,
                    e.Item.Artist,
                    e.Item.ImageUrl,
                    e.Item.DurationMs,
                    e.Item.RequestedBy,
                    e.Item.Cost,
                    e.Item.Code,
                    ReferenceEquals(e.Item, inFlight)
                ))
                .ToList();

        return new(nowPlaying, items);
    }

    public async Task<Result> AddToQueueAsync(
        string broadcasterId,
        string trackUri,
        string? requestedBy = null,
        CancellationToken cancellationToken = default,
        string? requesterUserId = null
    )
    {
        if (!Guid.TryParse(broadcasterId, out Guid tenantId))
            return InvalidChannelId();

        MusicConfigDto? config = await GetConfigOrNullAsync(broadcasterId, cancellationToken);
        if (config is { RequestsOpen: false })
            return Result.Failure(RequestsClosedMessage, RequestsClosedCode);

        Result? notAllowed = await RefuseIfSourceNotAllowedAsync(
            tenantId,
            config,
            ChatLinkText.Normalize(trackUri),
            cancellationToken
        );
        if (notAllowed is not null)
            return notAllowed;

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
            await ResolveOrSearchAsync(
                provider,
                tenantId,
                trackUri,
                cancellationToken,
                musicCategoryOnly: config?.YouTubeMusicCategoryOnly
                    ?? MusicConfigDefaults.YouTubeMusicCategoryOnly
            );
        // A verdict about the link itself refuses the add; only an outage degrades to placeholder metadata.
        if (IsVerdictOnTheLink(resolveFailure))
        {
            Result<MusicTrack> refused = ProviderFailureResult<MusicTrack>(
                provider,
                tenantId,
                resolveFailure
            );
            return Result.Failure(refused.ErrorMessage!, refused.ErrorCode);
        }
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
            cancellationToken,
            requesterUserId
        );
    }

    public async Task<Result<MusicTrack>> RequestTrackAsync(
        string broadcasterId,
        string query,
        string? requestedBy = null,
        int? requesterRoleLevel = null,
        CancellationToken cancellationToken = default,
        string? requesterUserId = null
    )
    {
        if (!Guid.TryParse(broadcasterId, out Guid tenantId))
            return InvalidChannelId<MusicTrack>();

        MusicConfigDto? config = await GetConfigOrNullAsync(broadcasterId, cancellationToken);
        if (config is not null)
        {
            if (!config.IsEnabled)
                return Result.Failure<MusicTrack>(
                    "Song requests are turned off in this channel.",
                    "SR_DISABLED"
                );

            if (!config.RequestsOpen)
                return Result.Failure<MusicTrack>(RequestsClosedMessage, RequestsClosedCode);

            if (requesterRoleLevel is { } roleLevel)
            {
                PermissionLevel floor = ParseMinTrustLevel(config.MinTrustLevel);
                if (roleLevel < floor.ToLevelValue())
                    return Result.Failure<MusicTrack>(
                        $"Song requests need at least {config.MinTrustLevel} right now.",
                        "MIN_TRUST_LEVEL",
                        errorData: new MusicRequestRefusal(TrustLevel: config.MinTrustLevel)
                    );
            }
        }

        // The old bot's strike rule: ten banned tracks and the person's own requests are refused.
        if (
            requesterUserId is not null
            && await _blockedTracks.CountByBlockerAsync(
                tenantId,
                requesterUserId,
                cancellationToken
            ) >= SongBanStrikes.RequestsRefusedFrom
        )
            return Result.Failure<MusicTrack>(
                "Stop requesting songs, your permission has been revoked",
                "SR_REVOKED"
            );

        Result? notAllowed = await RefuseIfSourceNotAllowedAsync(
            tenantId,
            config,
            ChatLinkText.Normalize(query),
            cancellationToken
        );
        if (notAllowed is not null)
            return Result.Failure<MusicTrack>(notAllowed.ErrorMessage!, notAllowed.ErrorCode);

        // A link goes to the provider that owns it, so a YouTube link is played on YouTube even when
        // Spotify is the active provider. A search phrase goes to the active provider.
        IMusicProvider? provider = await GetProviderForTrackAsync(
            tenantId,
            ChatLinkText.Normalize(query),
            cancellationToken
        );
        if (provider is null)
            return NoProvider<MusicTrack>();

        // Each music service has its own request role, checked on top of the channel's allow switch.
        Result? roleRefused = await SongRequestSourceAccess.RefuseIfBelowAsync(
            _db,
            tenantId,
            provider.Provider,
            requesterRoleLevel,
            cancellationToken
        );
        if (roleRefused is not null)
            return Result.Failure<MusicTrack>(roleRefused.ErrorMessage!, roleRefused.ErrorCode);

        // The length cap applies to a search pick too, so a candidate over it is passed over for the next
        // one instead of being picked and then refused. Operators are not held by the cap.
        int? maxDurationMs = IsOperator(requesterRoleLevel)
            ? null
            : (config?.MaxSongSeconds ?? DefaultMaxSongSeconds) * 1000;
        (TrackInfo? trackInfo, MusicProviderFailureReason failure) = await ResolveOrSearchAsync(
            provider,
            tenantId,
            query,
            cancellationToken,
            maxDurationMs,
            config?.YouTubeMusicCategoryOnly ?? MusicConfigDefaults.YouTubeMusicCategoryOnly
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
            return ProviderFailureResult<MusicTrack>(provider, tenantId, failure);
        }
        if (trackInfo is null)
            return Result.Failure<MusicTrack>($"No tracks found for \"{query}\".", "NOT_FOUND");

        Result enqueued = await EnqueueResolvedAsync(
            tenantId,
            broadcasterId,
            provider,
            trackInfo,
            requestedBy,
            cancellationToken,
            requesterUserId,
            requesterRoleLevel
        );
        if (enqueued.IsFailure)
        {
            RememberIfHeldByALimit(
                broadcasterId,
                trackInfo,
                requestedBy,
                requesterUserId,
                enqueued
            );
            return Result.Failure<MusicTrack>(
                enqueued.ErrorMessage!,
                enqueued.ErrorCode,
                enqueued.ErrorDetail,
                enqueued.ErrorData
            );
        }

        return Result.Success(ToMusicTrack(trackInfo));
    }

    public async Task<Result<MusicTrack>> AdmitPermittedRequestAsync(
        string broadcasterId,
        string requestedBy,
        CancellationToken cancellationToken = default
    )
    {
        if (!Guid.TryParse(broadcasterId, out Guid tenantId))
            return InvalidChannelId<MusicTrack>();

        RefusedSongRequest? held = _refused.Take(broadcasterId, requestedBy);
        if (held is null)
            return Result.Failure<MusicTrack>(
                "No song request is waiting for permission.",
                "NO_PENDING_REQUEST"
            );

        IMusicProvider? provider = await GetProviderForTrackAsync(
            tenantId,
            held.Track.TrackUri,
            cancellationToken
        );
        if (provider is null)
            return NoProvider<MusicTrack>();

        // Only the limit that held the request is skipped; the blocklist and the duplicate gate still apply.
        Result enqueued = await EnqueueResolvedAsync(
            tenantId,
            broadcasterId,
            provider,
            held.Track,
            held.RequestedBy,
            cancellationToken,
            held.RequesterUserId,
            admittedByPermission: true
        );
        return enqueued.IsFailure
            ? Result.Failure<MusicTrack>(
                enqueued.ErrorMessage!,
                enqueued.ErrorCode,
                enqueued.ErrorDetail,
                enqueued.ErrorData
            )
            : Result.Success(ToMusicTrack(held.Track));
    }

    private static MusicTrack ToMusicTrack(TrackInfo trackInfo) =>
        new(
            trackInfo.TrackUri,
            trackInfo.TrackName,
            trackInfo.Artist,
            trackInfo.Album,
            trackInfo.AlbumArtUrl,
            trackInfo.DurationMs,
            trackInfo.Provider
        );

    /// <summary>Keeps a request a limit refused, so a moderator's <c>!permit</c> can admit it without a new search.</summary>
    private void RememberIfHeldByALimit(
        string broadcasterId,
        TrackInfo trackInfo,
        string? requestedBy,
        string? requesterUserId,
        Result refusal
    )
    {
        if (
            requestedBy is not null
            && refusal.ErrorCode is "TRACK_TOO_LONG" or "QUEUE_FULL" or "PER_USER_LIMIT"
        )
            _refused.Remember(
                broadcasterId,
                new(requestedBy, requesterUserId, trackInfo, refusal.ErrorCode!)
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
    /// <summary>Spotify's search ranks a bigger page the way its own app does; the first of ten is picked from.</summary>
    private const int SearchCandidateCount = 10;

    private async Task<(TrackInfo? Track, MusicProviderFailureReason Failure)> ResolveOrSearchAsync(
        IMusicProvider provider,
        Guid tenantId,
        string queryOrUri,
        CancellationToken cancellationToken,
        int? maxDurationMs = null,
        bool musicCategoryOnly = MusicConfigDefaults.YouTubeMusicCategoryOnly
    )
    {
        // A pasted link is read the way chat writes it (<wrapped>, trailing "." or ")", no https://), so
        // the provider and the title lookup both see a plain absolute URL.
        string input = ChatLinkText.Normalize(queryOrUri);

        (TrackInfo? resolved, MusicProviderFailureReason resolveFailure) =
            await provider.ResolveTrackAsync(tenantId, input, cancellationToken);
        if (resolveFailure != MusicProviderFailureReason.None)
            return (null, resolveFailure);
        if (resolved is not null)
            return YouTubeMusicCategory.Admits(musicCategoryOnly, resolved, input)
                ? (resolved, MusicProviderFailureReason.None)
                : (null, MusicProviderFailureReason.NotMusic);

        string searchText = input;
        if (IsWebLink(input))
        {
            // A link is never searched as text: its URL finds an unrelated song. One the provider claims
            // but found nothing behind is "not found"; any other is searched by its title, when it has one.
            if (provider.OwnsLink(input))
                return (null, MusicProviderFailureReason.None);

            string? title = await _linkTitles.TryGetTitleAsync(input, cancellationToken);
            if (string.IsNullOrWhiteSpace(title))
                return (null, MusicProviderFailureReason.UnsupportedLink);
            searchText = title;
        }

        (IReadOnlyList<TrackInfo> hits, MusicProviderFailureReason searchFailure) =
            await provider.SearchAsync(
                tenantId,
                searchText,
                SearchCandidateCount,
                cancellationToken
            );
        IReadOnlyList<TrackInfo> admitted =
        [
            .. hits.Where(hit => YouTubeMusicCategory.Admits(musicCategoryOnly, hit, null)),
        ];
        return searchFailure != MusicProviderFailureReason.None
            ? (null, searchFailure)
            : (
                SongSearchRanker.Pick(searchText, WithinLength(admitted, maxDurationMs)),
                MusicProviderFailureReason.None
            );
    }

    /// <summary>The hits that fit the length cap; all of them when none does, so the pick is still refused
    /// with the honest "too long" reply rather than reading as "not found".</summary>
    private static IReadOnlyList<TrackInfo> WithinLength(
        IReadOnlyList<TrackInfo> hits,
        int? maxDurationMs
    )
    {
        if (maxDurationMs is not int max)
            return hits;
        List<TrackInfo> fitting = [.. hits.Where(hit => hit.DurationMs <= max)];
        return fitting.Count > 0 ? fitting : hits;
    }

    /// <summary>The provider answered about the link itself (it can never be played), not about its own health.</summary>
    private static bool IsVerdictOnTheLink(MusicProviderFailureReason failure) =>
        failure
            is MusicProviderFailureReason.UnsupportedLink
                or MusicProviderFailureReason.LiveBroadcast
                or MusicProviderFailureReason.TrackMissing
                or MusicProviderFailureReason.NotPlayableInRegion
                or MusicProviderFailureReason.UnreadableLink
                or MusicProviderFailureReason.UnsupportedContentType
                or MusicProviderFailureReason.UnknownLength
                or MusicProviderFailureReason.NotMusic;

    private static bool IsWebLink(string query) =>
        Uri.TryCreate(query.Trim(), UriKind.Absolute, out Uri? uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    /// <summary>
    /// Translates a provider's <see cref="MusicProviderFailureReason"/> into the caller-facing error code
    /// every SR entry point (the !sr builtin, the reward-pipeline action, the public SR page, scripts)
    /// already switches on — <c>MISSING_SCOPE</c> so the message tells the broadcaster/mod the connection
    /// needs attention, <c>PROVIDER_UNAVAILABLE</c> (distinct from this service's own
    /// <c>SERVICE_UNAVAILABLE</c> "no provider configured at all") so a transient outage never reads as
    /// "go connect Spotify" when it already IS connected, and <c>PROVIDER_NOT_CONFIGURED</c> when the
    /// operator never set the provider's app-level key (reconnecting would change nothing), and
    /// <c>PROVIDER_RATE_LIMITED</c> with the minutes left on the provider's cooldown, so the requester is
    /// told how long to wait instead of "temporarily unavailable" (2026-10-05: a 2h18m Spotify block read
    /// as an outage in chat).
    /// </summary>
    private static Result<T> ProviderFailureResult<T>(
        IMusicProvider provider,
        Guid tenantId,
        MusicProviderFailureReason failure
    ) =>
        failure switch
        {
            MusicProviderFailureReason.RateLimited => Result.Failure<T>(
                "The music service is rate-limiting this channel.",
                "PROVIDER_RATE_LIMITED",
                errorData: new MusicRequestRefusal(
                    RetryMinutes: RetryMinutesLeft(provider, tenantId)
                )
            ),
            MusicProviderFailureReason.NotConnected => Result.Failure<T>(
                "The music connection needs to be reconnected.",
                "MISSING_SCOPE"
            ),
            MusicProviderFailureReason.NotConfigured => Result.Failure<T>(
                "YouTube song requests are not set up on this bot yet. The bot owner must add a YouTube API key.",
                "PROVIDER_NOT_CONFIGURED"
            ),
            MusicProviderFailureReason.UnsupportedContentType => Result.Failure<T>(
                "Song requests only take individual tracks — that link is a playlist, album, episode, "
                    + "show, or artist page. Paste a single track link, or just search by name instead.",
                "UNSUPPORTED_CONTENT_TYPE"
            ),
            MusicProviderFailureReason.UnsupportedLink => Result.Failure<T>(
                UnsupportedLinkMessage,
                "UNSUPPORTED_LINK"
            ),
            MusicProviderFailureReason.NotPlayableInRegion => Result.Failure<T>(
                "That track can't play in the streamer's country. Try a different version of the song.",
                "TRACK_UNAVAILABLE"
            ),
            MusicProviderFailureReason.LiveBroadcast => Result.Failure<T>(
                "Live streams and premieres can't be requested.",
                "TRACK_IS_LIVE"
            ),
            MusicProviderFailureReason.TrackMissing => Result.Failure<T>(
                "That video doesn't exist or is private.",
                "TRACK_MISSING"
            ),
            MusicProviderFailureReason.UnreadableLink => Result.Failure<T>(
                "No track could be read from that link.",
                "UNREADABLE_LINK"
            ),
            MusicProviderFailureReason.UnknownLength => Result.Failure<T>(
                "The track's length could not be read, so the length limit cannot be checked.",
                "TRACK_LENGTH_UNKNOWN"
            ),
            MusicProviderFailureReason.NotMusic => Result.Failure<T>(
                "That video is not a music video, and song requests play music only.",
                "TRACK_NOT_MUSIC"
            ),
            MusicProviderFailureReason.QuotaExhausted => Result.Failure<T>(
                "The music service's daily request limit is used up.",
                "PROVIDER_QUOTA_EXHAUSTED",
                errorData: new MusicRequestRefusal(RetryAt: CoolingUntil(provider, tenantId))
            ),
            _ => Result.Failure<T>(
                "The music provider is temporarily unavailable.",
                "PROVIDER_UNAVAILABLE"
            ),
        };

    /// <summary>When the provider lets this channel call again; null when it tracks no cooldown.</summary>
    private static DateTimeOffset? CoolingUntil(IMusicProvider provider, Guid tenantId) =>
        provider.TryGetCoolingUntil(tenantId, out DateTimeOffset until) ? until : null;

    /// <summary>Whole minutes until the provider lets this channel call again, never below one: a
    /// cooldown the provider does not track (or that just ended) still reads as "try again in a minute".</summary>
    private static int RetryMinutesLeft(IMusicProvider provider, Guid tenantId)
    {
        if (!provider.TryGetCoolingUntil(tenantId, out DateTimeOffset until))
            return 1;
        double minutes = Math.Ceiling((until - DateTimeOffset.UtcNow).TotalMinutes);
        return Math.Max(1, (int)minutes);
    }

    /// <summary>A track the provider flagged as unplayable in an embedded player; null when it can play.</summary>
    private static Result? RefuseIfUnplayable(TrackInfo trackInfo)
    {
        MusicRequestRefusal refusal = new(trackInfo.TrackName, trackInfo.Artist);
        if (!trackInfo.IsEmbeddable)
            return Result.Failure(
                $"\"{trackInfo.TrackName}\" does not allow playback outside its own site.",
                "TRACK_NOT_EMBEDDABLE",
                errorData: refusal
            );
        if (trackInfo.IsAgeRestricted)
            return Result.Failure(
                $"\"{trackInfo.TrackName}\" is age-restricted and cannot play in an embedded player.",
                "TRACK_AGE_RESTRICTED",
                errorData: refusal
            );
        return null;
    }

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
        CancellationToken cancellationToken,
        string? requesterUserId = null,
        int? requesterRoleLevel = null,
        bool admittedByPermission = false
    )
    {
        string trackUri = trackInfo.TrackUri;

        // Blocklist admission gate (legacy !bansong): refused before the fair queue ever sees it.
        if (await _blockedTracks.IsBlockedAsync(tenantId, trackUri, cancellationToken))
            return Result.Failure(
                $"\"{trackInfo.TrackName}\" is blocked in this channel.",
                "TRACK_BLOCKED",
                errorData: new MusicRequestRefusal(trackInfo.TrackName, trackInfo.Artist)
            );

        // Playability gates: the overlay's embedded player can never play these, so no role skips them —
        // queuing one would announce "added" and then silently fail when its turn comes.
        Result? unplayable = RefuseIfUnplayable(trackInfo);
        if (unplayable is not null)
            return unplayable;

        // Broadcaster and moderators are trusted operators: like a command cooldown, the length, queue and
        // per-user limits exist to stop viewer abuse and never hold them (same ladder rung as the cooldown).
        // A moderator's !permit admits a held request the same way: it skips only the limits (never the blocklist).
        bool isOperator = admittedByPermission || IsOperator(requesterRoleLevel);

        MusicConfigDto? config = await GetConfigOrNullAsync(broadcasterId, cancellationToken);

        // Length cap: a track over the channel's longest-song dial never reaches the fair queue.
        int maxSongSeconds = config?.MaxSongSeconds ?? DefaultMaxSongSeconds;
        if (!isOperator && trackInfo.DurationMs > maxSongSeconds * 1000L)
            return Result.Failure(
                $"\"{trackInfo.TrackName}\" is longer than the {maxSongSeconds} second limit.",
                "TRACK_TOO_LONG",
                errorData: new MusicRequestRefusal(
                    trackInfo.TrackName,
                    trackInfo.Artist,
                    Limit: maxSongSeconds
                )
            );

        if (!isOperator && config is { AllowExplicit: false } && trackInfo.IsExplicit)
            return Result.Failure(
                $"\"{trackInfo.TrackName}\" is explicit, and explicit songs are not allowed in this channel.",
                "TRACK_EXPLICIT",
                errorData: new MusicRequestRefusal(trackInfo.TrackName, trackInfo.Artist)
            );

        string streamOwnerKey = requestedBy ?? "anonymous";
        if (
            !isOperator
            && config?.PerStreamLimit is int perStreamLimit
            && _queueStore.StreamRequestCount(broadcasterId, streamOwnerKey) >= perStreamLimit
        )
            return Result.Failure(
                $"You already requested {perStreamLimit} song(s) this stream — that is the limit.",
                "PER_STREAM_LIMIT",
                errorData: new MusicRequestRefusal(Limit: perStreamLimit)
            );

        FairQueue<SongRequestEntry> queue = _queueStore.GetOrCreate(broadcasterId);

        // Capacity admission gate: both AddToQueueAsync and RequestTrackAsync fold into this one
        // enqueue point, so MaxQueueSize/MaxRequestsPerUser are enforced here once rather than at
        // each caller — refused before the duplicate/provider-push work below ever runs.
        if (config is not null)
        {
            IReadOnlyList<(SongRequestEntry Item, int Rank, string OwnerKey)> snapshot =
                queue.GetSnapshot();
            if (!isOperator && snapshot.Count >= config.MaxQueueSize)
                return Result.Failure(
                    $"The queue is full ({config.MaxQueueSize} max) — try again once it's shorter.",
                    "QUEUE_FULL",
                    errorData: new MusicRequestRefusal(Limit: config.MaxQueueSize)
                );

            string ownerKey = requestedBy ?? "anonymous";
            int ownedCount = snapshot.Count(e =>
                string.Equals(e.OwnerKey, ownerKey, StringComparison.OrdinalIgnoreCase)
            );
            int perUserCap = EffectivePerUserCap(config, requesterRoleLevel);
            // An operator is held only by a cap the channel set for a role rung on purpose.
            bool capApplies =
                !admittedByPermission
                && (!isOperator || config.MaxRequestsPerRole is { Count: > 0 });
            if (capApplies && ownedCount >= perUserCap)
                return Result.Failure(
                    $"You already have {perUserCap} request(s) queued — wait for one to play before adding more.",
                    "PER_USER_LIMIT",
                    errorData: new MusicRequestRefusal(Limit: perUserCap)
                );
        }

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
            requestedBy ?? "anonymous",
            // No admission path charges for a song request yet (S067b) — every live request is free.
            Cost: 0,
            RequesterUserId: null,
            Code: NextSongCode(broadcasterId)
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
                alreadyQueued.Item?.RequestedBy ?? "someone",
                new MusicRequestRefusal(
                    trackInfo.TrackName,
                    trackInfo.Artist,
                    alreadyQueued.Item?.RequestedBy
                )
            );
        }
        _queueStore.RecordStreamRequest(broadcasterId, streamOwnerKey);
        await SyncPersistedQueueAsync(broadcasterId, queue, cancellationToken);

        // ONE track at a time reaches the provider. The fair queue is the authority on order, and it can
        // only stay that way while the tracks behind the current one are still OURS to re-rank: a viewer's
        // first request must be able to overtake someone's third, which is impossible once both sit in
        // Spotify's own queue in arrival order. So exactly one request is ever handed over — the one now
        // waiting to play — and SongRequestQueueReconciler hands over the next when the provider moves on.
        // A push failure means the request never reached the provider: it is NOT left behind as a phantom
        // entry pretending to be live — that one entry is removed (never the requester's other pending
        // requests) and the caller gets the real, typed reason instead of a false success.
        Result? pushFailure;
        using (await _queueStore.AcquireHandoverLockAsync(broadcasterId, cancellationToken))
            pushFailure = await HandOverAdmittedIfIdleAsync(
                tenantId,
                broadcasterId,
                provider,
                queue,
                entry,
                trackInfo.TrackName,
                cancellationToken
            );
        if (pushFailure is not null)
            return pushFailure;

        await LogAndAnnounceAsync(
            tenantId,
            broadcasterId,
            trackInfo,
            requestedBy,
            cancellationToken,
            requesterUserId
        );
        return Result.Success();
    }

    /// <summary>
    /// Tells the playback director about a just-admitted request when nothing of ours is in flight, and
    /// records it as in-flight when the director starts it. Returns null on success or when something is
    /// already in flight (the request then waits its turn in the fair queue, where a later re-rank can
    /// still move it); returns the typed failure after rolling the entry back when the director is
    /// Blocked. The caller holds the channel's hand-over lock.
    /// </summary>
    private async Task<Result?> HandOverAdmittedIfIdleAsync(
        Guid tenantId,
        string broadcasterId,
        IMusicProvider provider,
        FairQueue<SongRequestEntry> queue,
        SongRequestEntry entry,
        string trackName,
        CancellationToken cancellationToken
    )
    {
        if (_queueStore.GetInFlight(broadcasterId) is not null)
            return null;
        // An earlier request still waits for its engine (a YouTube head waits for Spotify's song to end):
        // this one queues behind it. Pushed now, it would play first and the reconciler would then drop
        // the waiting head as played (live 2026-10-18).
        if (!ReferenceEquals(queue.Peek(), entry))
            return null;

        // The director starts the head on its engine, silences the other engine first, and moves to
        // Blocked when the engine cannot start it. A chat command (`!sr`) has no HTTP request to inherit
        // a sanction from, so the send carries its own.
        using (
            _sanctions.Begin(OutboundSanction.ChannelConfiguration("music:song_request_dispatch"))
        )
            await _playbackInput.SendAsync(tenantId, new RequestAdded(), cancellationToken);

        PlaybackState state = _playbackState.StateOf(tenantId);
        if (state.Kind == PlaybackStateKind.Blocked)
            return await RollBackAsync(
                broadcasterId,
                queue,
                entry,
                BlockedOnQueue(provider, state.Reason, trackName),
                cancellationToken
            );

        // The director is Starting or Playing this request: remember it so the next request queues
        // behind it in OUR queue instead of being handed over too.
        if (!string.Equals(state.Item?.Uri, entry.TrackUri, StringComparison.OrdinalIgnoreCase))
            return null;

        _queueStore.SetLastEngine(broadcasterId, provider.Provider);
        _queueStore.SetInFlight(broadcasterId, entry);
        // Re-sync so the persisted row set carries the in-flight flag too (S-SR-INFLIGHT-DURABLE).
        await SyncPersistedQueueAsync(broadcasterId, queue, cancellationToken);
        return null;
    }

    /// <summary>
    /// Turns the director's Blocked reason into the typed refusal the requester sees. The reason is the
    /// engine's <see cref="EngineBlockReason"/> name; any other text is a provider problem.
    /// </summary>
    private static Result BlockedOnQueue(
        IMusicProvider provider,
        string? reason,
        string trackName
    ) =>
        Enum.TryParse(reason, out EngineBlockReason block)
            ? block switch
            {
                EngineBlockReason.NotConnected => AuthFailedOnQueue(trackName),
                EngineBlockReason.Forbidden or EngineBlockReason.PremiumRequired =>
                    ForbiddenOnQueue(trackName),
                EngineBlockReason.NoOutput when provider.Provider == "youtube" =>
                    NoOverlayPlayerOnQueue(trackName),
                EngineBlockReason.NoOutput => NoActiveDeviceOnQueue(trackName),
                _ => ProviderErrorOnQueue(trackName),
            }
            : ProviderErrorOnQueue(trackName);

    /// <summary>
    /// Sends an owner control (pause, resume, previous, seek, volume) to the playback director, which commands
    /// the engine that plays and no other. An engine that refuses leaves the director Blocked and that reason is
    /// the answer; an Idle director has nothing to control.
    /// </summary>
    private async Task<Result> SendOwnerControlAsync(
        Guid tenantId,
        IMusicProvider provider,
        PlaybackInput input,
        CancellationToken cancellationToken
    )
    {
        await _playbackInput.SendAsync(tenantId, input, cancellationToken);

        PlaybackState state = _playbackState.StateOf(tenantId);
        return state.Kind switch
        {
            PlaybackStateKind.Blocked
                when state.Reason == nameof(EngineBlockReason.PremiumRequired) => PremiumRequired(
                new PremiumRequiredException(provider.Provider)
            ),
            PlaybackStateKind.Blocked => BlockedOnSkip(provider, state.Reason),
            PlaybackStateKind.Idle => NothingPlaying(provider.Provider),
            _ => Result.Success(),
        };
    }

    /// <summary>Turns the director's Blocked reason into the typed refusal the skipper sees.</summary>
    private static Result BlockedOnSkip(IMusicProvider provider, string? reason) =>
        Enum.TryParse(reason, out EngineBlockReason block)
            ? block switch
            {
                EngineBlockReason.NotConnected => Result.Failure(
                    "Couldn't skip — the music connection needs to be reconnected.",
                    "MUSIC_AUTH_FAILED"
                ),
                EngineBlockReason.Forbidden or EngineBlockReason.PremiumRequired => Result.Failure(
                    "Couldn't skip — the music connection doesn't have permission for that.",
                    "MUSIC_FORBIDDEN"
                ),
                EngineBlockReason.NoOutput => NothingPlaying(provider.Provider),
                _ => ProviderErrorOnSkip(),
            }
            : ProviderErrorOnSkip();

    private static Result ProviderErrorOnSkip() =>
        Result.Failure(
            "Couldn't skip — the music service had a problem. Try again in a moment.",
            "PROVIDER_ERROR"
        );

    /// <summary>The accepted-request side effects, identical whether the request went straight to the
    /// provider or is waiting its turn: the log line, the analytics fact, and the fresh queue snapshot the
    /// dashboard and sr_queue overlay render.</summary>
    private async Task LogAndAnnounceAsync(
        Guid tenantId,
        string broadcasterId,
        TrackInfo trackInfo,
        string? requestedBy,
        CancellationToken cancellationToken,
        string? requesterUserId = null
    )
    {
        await RecordRequestHistoryAsync(tenantId, trackInfo, requesterUserId, cancellationToken);

        _logger.LogInformation(
            "Queued track '{Track}' for {BroadcasterId} (requested by {RequestedBy})",
            trackInfo.TrackName,
            broadcasterId,
            requestedBy
        );

        // The accepted-request fact the analytics fold (SongRequests, a viewer's TotalSongRequests) and future SR
        // surfaces consume. UserId is the requester key the fair queue records; RequesterUserId is the viewer's
        // platform id, null when no viewer is behind the request.
        await _eventBus.PublishAsync(
            new SongRequestedEvent
            {
                BroadcasterId = tenantId,
                UserId = requestedBy ?? "anonymous",
                UserDisplayName = requestedBy ?? "anonymous",
                RequesterUserId = requesterUserId,
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

        IMusicProvider? provider = await VolumeProviderAsync(tenantId, cancellationToken);
        if (provider is null)
            return NoProvider();
        if (!HasCapability(provider, MusicProviderCapabilities.Volume))
            return Unsupported("volume control");

        // Reachable off a pipeline action or the !songvolume builtin with no HTTP request of its own —
        // same no-person-present shape as HandOverNextAsync's already-fixed gap.
        using IDisposable sanction = _sanctions.Begin(
            OutboundSanction.ChannelConfiguration("music:volume_control")
        );

        return await SendOwnerControlAsync(
            tenantId,
            provider,
            new OwnerVolume(volume),
            cancellationToken
        );
    }

    /// <summary>
    /// The provider whose volume a change must reach: the engine playing the channel's requests (the YouTube
    /// overlay player while a request plays there, even though Spotify is the default provider), else the
    /// active provider.
    /// </summary>
    private async Task<IMusicProvider?> VolumeProviderAsync(
        Guid tenantId,
        CancellationToken cancellationToken
    )
    {
        if (_queueStore.GetLastEngine(tenantId.ToString()) is { } engine)
        {
            MusicConfigDto? config = await GetConfigOrNullAsync(
                tenantId.ToString(),
                cancellationToken
            );
            IMusicProvider? playing = (
                await GetAllowedProvidersAsync(tenantId, config, cancellationToken)
            ).FirstOrDefault(p =>
                p.Provider == engine && HasCapability(p, MusicProviderCapabilities.Volume)
            );
            if (playing is not null)
                return playing;
        }
        return await GetActiveProviderAsync(tenantId, cancellationToken);
    }

    public async Task<NowPlaying?> GetNowPlayingAsync(
        string broadcasterId,
        CancellationToken cancellationToken = default,
        bool isBackgroundPoll = false
    )
    {
        if (!Guid.TryParse(broadcasterId, out Guid tenantId))
            return null;

        IMusicProvider? provider = await GetActiveProviderAsync(tenantId, cancellationToken);
        if (provider is null || !HasCapability(provider, MusicProviderCapabilities.NowPlaying))
            return null;

        if (
            !isBackgroundPoll
            && _nowPlayingCache.TryGet(tenantId, ReaderCacheFreshness) is { } fresh
        )
            return ToNowPlaying(
                TrackAsOfNow(fresh, fresh.Track.IsPlaying),
                RequesterOfPlayingTrack(_queueStore, broadcasterId, fresh.Track.TrackUri)
            );

        // A reader that arrives right after "nothing playing" gets the same answer: without this every
        // Stream Deck key / overlay / chat command on a silent channel sent its own Spotify call.
        if (!isBackgroundPoll && _nowPlayingCache.IsKnownEmpty(tenantId, ReaderCacheFreshness))
            return null;

        TrackInfo? track = await provider.GetCurrentTrackAsync(
            tenantId,
            cancellationToken,
            isBackgroundPoll
        );
        if (track is null)
        {
            // Only a healthy connection's "nothing playing" is worth repeating; a dead token or a refusal
            // must stay retryable the moment it recovers.
            if (await GetActiveProviderAuthStatusAsync(broadcasterId, cancellationToken) is null)
                _nowPlayingCache.SetEmpty(tenantId, DateTimeOffset.UtcNow);
            return null;
        }

        _nowPlayingCache.Set(tenantId, track, DateTimeOffset.UtcNow);

        return ToNowPlaying(
            track,
            RequesterOfPlayingTrack(_queueStore, broadcasterId, track.TrackUri)
        );
    }

    /// <summary>
    /// Who asked for the track the provider is playing, or null when nobody did (the streamer started it
    /// themselves, or it is whatever their playlist rolled onto next).
    ///
    /// <para>
    /// The in-flight entry is the ONE request handed to the provider, so it is the only candidate — but it
    /// is matched on the track uri rather than trusted blindly. The provider moves on by itself the moment a
    /// track ends, and without that check the last requester's name would stay stuck on every track that
    /// followed, crediting them for songs they never asked for.
    /// </para>
    /// </summary>
    /// <summary>
    /// A song code no other request in this channel is using — the pending queue AND the one already handed
    /// to the provider, which is still referable by code until it finishes.
    ///
    /// <para>
    /// Falls back to an empty code rather than failing the request: a song request that succeeds without a
    /// handle is a small loss, a song request refused because the bot could not name it is a real one.
    /// </para>
    /// </summary>
    private string NextSongCode(string broadcasterId)
    {
        HashSet<string> taken = new(StringComparer.Ordinal);

        FairQueue<SongRequestEntry>? queue = _queueStore.TryGet(broadcasterId);
        if (queue is not null)
        {
            foreach ((SongRequestEntry queued, int _, string _) in queue.GetSnapshot())
            {
                if (!string.IsNullOrEmpty(queued.Code))
                    taken.Add(queued.Code);
            }
        }

        SongRequestEntry? inFlight = _queueStore.GetInFlight(broadcasterId);
        if (inFlight is not null && !string.IsNullOrEmpty(inFlight.Code))
            taken.Add(inFlight.Code);

        return SongCode.NextAvailable(taken) ?? string.Empty;
    }

    internal static string? RequesterOfPlayingTrack(
        ISongRequestQueueStore queueStore,
        string broadcasterId,
        string? playingTrackUri
    )
    {
        if (string.IsNullOrEmpty(playingTrackUri))
            return null;

        SongRequestEntry? inFlight = queueStore.GetInFlight(broadcasterId);
        if (string.Equals(inFlight?.TrackUri, playingTrackUri, StringComparison.Ordinal))
            return inFlight?.RequestedBy;

        SongRequestEntry? playing = queueStore.GetPlayingRequest(broadcasterId);

        return string.Equals(playing?.TrackUri, playingTrackUri, StringComparison.Ordinal)
            ? playing?.RequestedBy
            : null;
    }

    public async Task<bool?> TryGetCachedIsPlayingAsync(
        string broadcasterId,
        CancellationToken cancellationToken = default
    )
    {
        if (!Guid.TryParse(broadcasterId, out Guid tenantId))
            return null;
        return _nowPlayingCache.TryGet(tenantId, NowPlayingCacheFreshness)?.Track.IsPlaying;
    }

    private static NowPlaying ToNowPlaying(TrackInfo track, string? requestedBy = null) =>
        new(
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
            requestedBy,
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
            track.CanResume,
            track.DeviceId,
            track.DeviceName
        );

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
    /// <summary>
    /// Maps a <c>MusicConfigDto.MinTrustLevel</c> token (<c>everyone|subscribers|vip|moderators|broadcaster</c>,
    /// validated by <c>UpdateMusicConfigDto</c>'s regex) onto the canonical <see cref="PermissionLevel"/> ladder.
    /// Deliberately NOT <c>ChatRole.Parse</c>: that parser's plural-free vocabulary (<c>subscriber</c>,
    /// <c>moderator</c>) would silently default this config's plural tokens (<c>subscribers</c>,
    /// <c>moderators</c>) to <see cref="PermissionLevel.Everyone"/> — a floor that reads as configured but
    /// gates nothing.
    /// </summary>
    private static PermissionLevel ParseMinTrustLevel(string minTrustLevel) =>
        minTrustLevel.Trim().ToLowerInvariant() switch
        {
            "subscribers" => PermissionLevel.Subscriber,
            "vip" => PermissionLevel.Vip,
            "moderators" => PermissionLevel.Moderator,
            "broadcaster" => PermissionLevel.Broadcaster,
            _ => PermissionLevel.Everyone,
        };

    private static readonly IReadOnlyDictionary<string, PermissionLevel> RoleCapRungs =
        new Dictionary<string, PermissionLevel>
        {
            ["viewer"] = PermissionLevel.Everyone,
            ["subscriber"] = PermissionLevel.Subscriber,
            ["vip"] = PermissionLevel.Vip,
            ["moderator"] = PermissionLevel.Moderator,
            ["broadcaster"] = PermissionLevel.Broadcaster,
        };

    /// <summary>True for the moderator rung and everything above it, broadcaster included. A caller with
    /// no level (the dashboard, the public page) is not an operator here.</summary>
    private static bool IsOperator(int? requesterRoleLevel) =>
        requesterRoleLevel is { } level && level >= PermissionLevel.Moderator.ToLevelValue();

    /// <summary>
    /// The most requests one person may have queued: the highest cap among the role rungs their level
    /// reaches, where a rung with no value counts as <c>MaxRequestsPerUser</c>. A caller with no level
    /// (the dashboard, the public page) gets <c>MaxRequestsPerUser</c>.
    /// </summary>
    private static int EffectivePerUserCap(MusicConfigDto config, int? requesterRoleLevel)
    {
        if (
            requesterRoleLevel is not { } level
            || config.MaxRequestsPerRole is not { Count: > 0 } caps
        )
            return config.MaxRequestsPerUser;

        int best = 0;
        foreach ((string role, PermissionLevel rung) in RoleCapRungs)
        {
            if (level < rung.ToLevelValue())
                continue;
            best = Math.Max(
                best,
                caps.TryGetValue(role, out int cap) ? cap : config.MaxRequestsPerUser
            );
        }
        return best;
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    /// <summary>Fetches the channel's <see cref="MusicConfigDto"/>, collapsing a failed lookup to
    /// <c>null</c> — every caller treats "no config" the same way: fall through as if nothing is
    /// configured, rather than refusing the request over a config-service outage.</summary>
    private async Task<MusicConfigDto?> GetConfigOrNullAsync(
        string broadcasterId,
        CancellationToken cancellationToken
    )
    {
        Result<MusicConfigDto> result = await _config.GetConfigAsync(
            broadcasterId,
            cancellationToken
        );
        return result.IsSuccess ? result.Value : null;
    }

    /// <summary>
    /// Resolves the channel's active provider: the registered providers the channel can use
    /// (<see cref="UsableMusicProviders"/> — connected, or usable on the app-level key alone), narrowed by
    /// the config's <c>AllowSpotify</c>/<c>AllowYouTube</c> toggles. A non-"auto" <c>PreferredProvider</c>
    /// wins outright when it's among the allowed, usable candidates; otherwise falls back to the interim
    /// priority rule (prefers a provider that can drive playback, keeping today's Spotify-before-YouTube
    /// ordering without naming either).
    /// </summary>
    private async Task<IMusicProvider?> GetActiveProviderAsync(
        Guid tenantId,
        CancellationToken cancellationToken
    )
    {
        MusicConfigDto? config = await GetConfigOrNullAsync(tenantId.ToString(), cancellationToken);
        IReadOnlyList<IMusicProvider> candidates = await GetAllowedProvidersAsync(
            tenantId,
            config,
            cancellationToken
        );

        if (config is { PreferredProvider: "spotify" or "youtube" } preferring)
        {
            IMusicProvider? preferred = candidates.FirstOrDefault(p =>
                p.Provider == preferring.PreferredProvider
            );
            if (preferred is not null)
                return preferred;
        }

        IMusicProvider? provider = candidates
            .OrderByDescending(p => HasCapability(p, MusicProviderCapabilities.PlaybackControl))
            .ThenBy(p => p.Provider, StringComparer.Ordinal)
            .FirstOrDefault();

        if (provider is null)
            _logger.LogDebug("No active music provider for broadcaster {BroadcasterId}", tenantId);

        return provider;
    }

    /// <summary>The providers the channel can use and has switched on.</summary>
    private async Task<IReadOnlyList<IMusicProvider>> GetAllowedProvidersAsync(
        Guid tenantId,
        MusicConfigDto? config,
        CancellationToken cancellationToken
    )
    {
        IReadOnlyList<IMusicProvider> usable = await UsableMusicProviders.ForChannelAsync(
            _db,
            _providers,
            tenantId,
            cancellationToken
        );
        return [.. usable.Where(p => IsAllowed(p.Provider, config))];
    }

    /// <summary>
    /// The provider a track belongs to, so a mixed queue plays each item on its own engine. A track no
    /// allowed provider claims goes to the active provider, which is today's one-provider behavior.
    /// </summary>
    private async Task<IMusicProvider?> GetProviderForTrackAsync(
        Guid tenantId,
        string trackUri,
        CancellationToken cancellationToken
    )
    {
        MusicConfigDto? config = await GetConfigOrNullAsync(tenantId.ToString(), cancellationToken);
        IReadOnlyList<IMusicProvider> allowed = await GetAllowedProvidersAsync(
            tenantId,
            config,
            cancellationToken
        );
        IMusicProvider? owner = allowed.FirstOrDefault(p => p.OwnsTrackUri(trackUri));
        return owner ?? await GetActiveProviderAsync(tenantId, cancellationToken);
    }

    private static bool IsAllowed(string providerKey, MusicConfigDto? config) =>
        config is null
        || providerKey switch
        {
            "spotify" => config.AllowSpotify,
            "youtube" => config.AllowYouTube,
            _ => true,
        };

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

    /// <summary>NO_ACTIVE_DEVICE, naming the provider that found no player so chat can say how to fix it.</summary>
    private static Result NothingPlaying(string provider) =>
        Result.Failure(
            "Nothing is playing on any device right now.",
            "NO_ACTIVE_DEVICE",
            errorData: new NoActivePlayerRefusal(provider)
        );

    private static Result PremiumRequired(PremiumRequiredException ex) =>
        Result.Failure(ex.Message, "PREMIUM_REQUIRED");

    // ── Queue-admission failure replies (!sr / AddToQueueAsync) ────────────────
    // Each carries the track name, since the viewer just asked for that specific song and the reply
    // needs to make clear which request failed and whether trying again is worth it.

    private static Result NoActiveDeviceOnQueue(string trackName) =>
        Result.Failure(
            $"Couldn't queue \"{trackName}\" — nothing is playing on any device right now. Start playback and try again.",
            "NO_ACTIVE_DEVICE",
            errorData: new MusicRequestRefusal(trackName)
        );

    private static Result NoOverlayPlayerOnQueue(string trackName) =>
        Result.Failure(
            $"Couldn't queue \"{trackName}\" — the audio overlay is not open. Open it in OBS to play YouTube requests.",
            "NO_OVERLAY_PLAYER",
            errorData: new MusicRequestRefusal(trackName)
        );

    private static Result AuthFailedOnQueue(string trackName) =>
        Result.Failure(
            $"Couldn't queue \"{trackName}\" — the music connection needs to be reconnected.",
            "MUSIC_AUTH_FAILED",
            errorData: new MusicRequestRefusal(trackName)
        );

    private static Result ForbiddenOnQueue(string trackName) =>
        Result.Failure(
            $"Couldn't queue \"{trackName}\" — the music connection doesn't have permission for that.",
            "MUSIC_FORBIDDEN",
            errorData: new MusicRequestRefusal(trackName)
        );

    private static Result ProviderErrorOnQueue(string trackName) =>
        Result.Failure(
            $"Couldn't queue \"{trackName}\" — the music service had a problem. Try again in a moment.",
            "PROVIDER_ERROR",
            errorData: new MusicRequestRefusal(trackName)
        );

    public async Task<bool> RemoveFromQueueAsync(
        string broadcasterId,
        int position,
        CancellationToken cancellationToken = default
    ) =>
        await RetractRequestAsync(broadcasterId, position, cancellationToken)
        != RequestRetraction.NotRemoved;

    public async Task<int> RemoveAllFromRequesterAsync(
        string broadcasterId,
        string requestedBy,
        CancellationToken cancellationToken = default
    )
    {
        int removedCount = 0;
        FairQueue<SongRequestEntry>? queue = _queueStore.TryGet(broadcasterId);
        int guard = queue?.GetSnapshot().Count ?? 0;

        for (int attempt = 0; attempt < guard; attempt++)
        {
            IReadOnlyList<(SongRequestEntry Item, int Rank, string OwnerKey)> snapshot =
                queue!.GetSnapshot();
            int position = -1;
            for (int index = 0; index < snapshot.Count; index++)
            {
                if (
                    string.Equals(
                        snapshot[index].Item.RequestedBy,
                        requestedBy,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    position = index;
                    break;
                }
            }

            if (
                position < 0
                || !await RemoveFromQueueAsync(broadcasterId, position, cancellationToken)
            )
                break;

            removedCount++;
        }

        return removedCount;
    }

    public async Task<RequestRetraction> RetractRequestAsync(
        string broadcasterId,
        int position,
        CancellationToken cancellationToken = default
    )
    {
        FairQueue<SongRequestEntry>? queue = _queueStore.TryGet(broadcasterId);
        IReadOnlyList<(SongRequestEntry Item, int Rank, string OwnerKey)> snapshotBeforeRemove =
            queue?.GetSnapshot() ?? [];
        SongRequestEntry? removedEntry =
            position >= 0 && position < snapshotBeforeRemove.Count
                ? snapshotBeforeRemove[position].Item
                : null;
        bool removed = queue is not null && queue.RemoveAt(position);
        bool willSkip = false;

        if (removed)
        {
            // Removing the in-flight entry must free the in-flight slot too — otherwise a moderator
            // clearing a stuck request leaves in-flight pointed at an entry that no longer exists
            // anywhere (SongRequestQueueReconciler only clears it by matching this exact track against
            // a real playback change, which never arrives for one removed here).
            bool wasInFlight =
                removedEntry is not null
                && ReferenceEquals(_queueStore.GetInFlight(broadcasterId), removedEntry);
            if (wasInFlight)
            {
                _queueStore.SetInFlight(broadcasterId, null);
            }

            // Spotify cannot delete from its own queue, so a removed track it still holds would play.
            // Our in-flight pointer is only a belief about that (it is cleared by a restart, a false
            // drop, a hand-over race), so Spotify's LIVE queue decides: arm a marker and
            // SongRetractionSkipHandler skips exactly this track when it starts. Songs the streamer
            // queued by hand are never touched.
            if (removedEntry is not null)
            {
                willSkip = await IsHeldByProviderAsync(
                    broadcasterId,
                    removedEntry,
                    wasInFlight,
                    cancellationToken
                );
                if (willSkip)
                    _queueStore.ArmRetraction(broadcasterId, removedEntry.TrackUri);
            }

            await SyncPersistedQueueAsync(broadcasterId, queue!, cancellationToken);
            if (Guid.TryParse(broadcasterId, out Guid tenantId))
            {
                if (removedEntry is not null)
                    await RefundIfPaidAsync(tenantId, removedEntry, cancellationToken);
                await PublishQueueChangedAsync(tenantId, broadcasterId, cancellationToken);
            }
        }

        if (!removed)
            return RequestRetraction.NotRemoved;

        return willSkip ? RequestRetraction.RemovedWillSkip : RequestRetraction.Removed;
    }

    /// <summary>
    /// Whether the provider still holds <paramref name="entry"/> in its own queue, read live. An in-flight
    /// entry is held by definition; for anything else the provider's queue answers. When that queue cannot
    /// be read the answer is "no" — nothing is armed on a guess, because a stray marker would skip a later
    /// legitimate play of the same song.
    /// </summary>
    private async Task<bool> IsHeldByProviderAsync(
        string broadcasterId,
        SongRequestEntry entry,
        bool wasInFlight,
        CancellationToken cancellationToken
    )
    {
        if (wasInFlight)
            return true;
        if (!Guid.TryParse(broadcasterId, out Guid tenantId))
            return false;

        try
        {
            IMusicProvider? provider = await GetActiveProviderAsync(tenantId, cancellationToken);
            IReadOnlyList<TrackInfo>? providerQueue = provider is null
                ? null
                : await provider.GetQueueAsync(tenantId, cancellationToken);
            return providerQueue?.Any(t => IsSameTrack(t.TrackUri, entry.TrackUri)) == true;
        }
        catch (Exception ex)
            when (ex
                    is PremiumRequiredException
                        or NoActiveDeviceException
                        or MusicAuthenticationFailedException
                        or MusicForbiddenException
            )
        {
            _logger.LogWarning(
                ex,
                "Could not read the provider queue to retract '{Track}' for {BroadcasterId}.",
                entry.TrackName,
                broadcasterId
            );
            return false;
        }
    }

    /// <summary>Matches the request already handed to the provider, which an operator move never crosses.</summary>
    private Func<SongRequestEntry, bool> IsInFlight(string broadcasterId)
    {
        SongRequestEntry? inFlight = _queueStore.GetInFlight(broadcasterId);
        return entry => inFlight is not null && ReferenceEquals(entry, inFlight);
    }

    public async Task<bool> PromoteToTopAsync(
        string broadcasterId,
        int position,
        CancellationToken cancellationToken = default
    )
    {
        FairQueue<SongRequestEntry>? queue = _queueStore.TryGet(broadcasterId);
        bool moved = queue is not null && queue.MoveToFront(position, IsInFlight(broadcasterId));

        if (moved)
        {
            await SyncPersistedQueueAsync(broadcasterId, queue!, cancellationToken);
            if (Guid.TryParse(broadcasterId, out Guid tenantId))
                await PublishQueueChangedAsync(tenantId, broadcasterId, cancellationToken);
        }

        return moved;
    }

    public async Task<Result> MoveAsync(
        string broadcasterId,
        int fromPosition,
        int toPosition,
        CancellationToken cancellationToken = default
    )
    {
        FairQueue<SongRequestEntry>? queue = _queueStore.TryGet(broadcasterId);
        int count = queue?.Count ?? 0;
        if (fromPosition < 0 || fromPosition >= count)
            return Result.Failure($"No queue item at position {fromPosition}.", "NOT_FOUND");
        if (toPosition < 0 || toPosition >= count)
            return Result.Failure(
                $"Position {toPosition} is outside the queue.",
                "VALIDATION_FAILED"
            );

        Func<SongRequestEntry, bool> isInFlight = IsInFlight(broadcasterId);
        if (fromPosition == 0 && isInFlight(queue!.Peek()!))
            return Result.Failure(
                "The song already sent to the player cannot move.",
                "VALIDATION_FAILED"
            );

        if (!queue!.MoveTo(fromPosition, toPosition, isInFlight))
            return Result.Failure("The queue changed while moving; try again.", "NOT_FOUND");

        if (fromPosition != toPosition)
        {
            await SyncPersistedQueueAsync(broadcasterId, queue, cancellationToken);
            if (Guid.TryParse(broadcasterId, out Guid tenantId))
                await PublishQueueChangedAsync(tenantId, broadcasterId, cancellationToken);
        }

        return Result.Success();
    }

    public async Task<Result<BlockedTrackDto>> BanQueuedTrackAsync(
        string broadcasterId,
        int position,
        string? blockedByUserId = null,
        CancellationToken cancellationToken = default
    )
    {
        if (!Guid.TryParse(broadcasterId, out Guid tenantId))
            return Result.Failure<BlockedTrackDto>("Invalid channel id.", "VALIDATION_FAILED");

        FairQueue<SongRequestEntry>? queue = _queueStore.TryGet(broadcasterId);
        IReadOnlyList<(SongRequestEntry Item, int Rank, string OwnerKey)> snapshot =
            queue?.GetSnapshot() ?? [];
        if (position < 0 || position >= snapshot.Count)
            return Result.Failure<BlockedTrackDto>(
                $"No queue item at position {position}.",
                "NOT_FOUND"
            );

        SongRequestEntry target = snapshot[position].Item;

        // A queued request carries no provider tag of its own (SongRequestEntry), so the ban is filed
        // against the channel's currently active provider — the same provider every queued track in a
        // channel's single fair queue was resolved against.
        string provider =
            await GetActiveProviderKeyAsync(broadcasterId, cancellationToken) ?? "unknown";

        Result<BlockedTrackDto> blocked = await _blockedTracks.BlockAsync(
            tenantId,
            new BlockTrackRequest(
                provider,
                target.TrackUri,
                target.TrackName,
                Reason: "Banned from dashboard queue",
                BlockedByUserId: blockedByUserId
            ),
            cancellationToken
        );
        if (blocked.IsFailure)
            return blocked;

        // Now-blocked entries have no business staying queued — remove it before it's ever handed to
        // the provider.
        queue!.RemoveAt(position);
        await SyncPersistedQueueAsync(broadcasterId, queue, cancellationToken);
        await RefundIfPaidAsync(tenantId, target, cancellationToken);
        await PublishQueueChangedAsync(tenantId, broadcasterId, cancellationToken);

        return blocked;
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

        // PlayOnceResumeHandler calls this off a PlaybackStateChangedEvent the poller published on its
        // own schedule — no person present, no HTTP request behind it — so it carries no ambient
        // sanction of its own to inherit.
        using IDisposable sanction = _sanctions.Begin(
            OutboundSanction.ChannelConfiguration("music:seek_control")
        );

        return await SendOwnerControlAsync(
            tenantId,
            provider,
            new OwnerSeek(positionMs),
            cancellationToken
        );
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

        // Reachable off a pipeline action with no HTTP request of its own — same no-person-present
        // shape as the other pipeline entry points.
        using IDisposable sanction = _sanctions.Begin(
            OutboundSanction.ChannelConfiguration("music:shuffle_control")
        );

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

        // Reachable off a pipeline action with no HTTP request of its own — same no-person-present
        // shape as the other pipeline entry points.
        using IDisposable sanction = _sanctions.Begin(
            OutboundSanction.ChannelConfiguration("music:repeat_control")
        );

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

        // Reachable off a pipeline action with no HTTP request of its own — same no-person-present
        // shape as the other pipeline entry points.
        using IDisposable sanction = _sanctions.Begin(
            OutboundSanction.ChannelConfiguration("music:device_transfer_control")
        );

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
            .Select(p => new MusicPlaylistDto(
                p.Id,
                p.Name,
                p.Uri,
                p.TrackCount,
                p.ImageUrl,
                provider.Provider
            ))
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

    /// <summary>How stale a cached <see cref="TrackInfo"/> may be before <see cref="PublishPlaybackStateChangedAsync"/>
    /// refuses to trust it for the assumed-outcome fast path — generous relative to the 1s poller cadence that
    /// keeps it warm, tight enough that a channel the poller has stopped reaching (backed off after failures)
    /// falls back to a real fetch instead of publishing on data that's actually gone stale.</summary>
    private static readonly TimeSpan NowPlayingCacheFreshness = TimeSpan.FromSeconds(3);

    // A reader (Stream Deck key, overlay, dashboard, chat command) takes the poller's answer while it is at most
    // this old, the owner's "no more than 1 second of drift from Spotify". Without it every reader sent its own
    // Spotify call on top of the poller's, and a channel's Spotify app was blocked for hours (live 2026-10-01/02).
    private static readonly TimeSpan ReaderCacheFreshness = TimeSpan.FromSeconds(1);

    /// <summary>
    /// The snapshot's track as it stands now: progress moved on by the time since it was observed, but only if
    /// the track was ALREADY playing then (a paused track's position does not advance on its own), and never
    /// past the end of the track.
    /// </summary>
    private static TrackInfo TrackAsOfNow(NowPlayingSnapshot snapshot, bool isPlaying)
    {
        TrackInfo cached = snapshot.Track;
        int progressMs = cached.IsPlaying
            ? cached.ProgressMs
                + (int)(DateTimeOffset.UtcNow - snapshot.ObservedAt).TotalMilliseconds
            : cached.ProgressMs;
        return new TrackInfo
        {
            TrackName = cached.TrackName,
            Artist = cached.Artist,
            Album = cached.Album,
            TrackUri = cached.TrackUri,
            AlbumArtUrl = cached.AlbumArtUrl,
            DurationMs = cached.DurationMs,
            Provider = cached.Provider,
            ProviderTrackId = cached.ProviderTrackId,
            ArtistId = cached.ArtistId,
            IsExplicit = cached.IsExplicit,
            IsAgeRestricted = cached.IsAgeRestricted,
            IsEmbeddable = cached.IsEmbeddable,
            IsPlaying = isPlaying,
            ProgressMs =
                cached.DurationMs > 0 ? Math.Min(progressMs, cached.DurationMs) : progressMs,
            VolumePercent = cached.VolumePercent,
            ShuffleEnabled = cached.ShuffleEnabled,
            RepeatMode = cached.RepeatMode,
            CanSetShuffle = cached.CanSetShuffle,
            CanSetRepeat = cached.CanSetRepeat,
            CanSkipNext = cached.CanSkipNext,
            CanSkipPrevious = cached.CanSkipPrevious,
            CanSeek = cached.CanSeek,
            CanPause = cached.CanPause,
            CanResume = cached.CanResume,
        };
    }

    /// <summary>
    /// Publishes <see cref="PlaybackStateChangedEvent"/> right after a successful mutation (play/pause/skip/
    /// play-context) so the dashboard + overlay + Stream Deck update instantly instead of waiting for the next
    /// <c>MusicStatePollingService</c> tick.
    ///
    /// <paramref name="assumeIsPlaying"/> is the ONE thing a Pause/Play mutation already knows for certain
    /// without asking the provider again — pausing/resuming doesn't change which track is loaded, only whether
    /// it's playing. When it's given and a fresh <see cref="INowPlayingCache"/> entry exists (kept warm by every
    /// real <see cref="GetNowPlayingAsync"/> read, i.e. every poller tick for an actively streaming channel),
    /// this skips the provider round trip entirely and publishes from the cached track with just that flag
    /// substituted — cutting the mutation-to-visible-update latency roughly in half by removing a second
    /// sequential Spotify API call from the critical path, and sidestepping a real Spotify quirk where a GET
    /// issued immediately after a PUT can still echo the PRE-mutation state (the read races the provider's own
    /// propagation, not just network latency). <paramref name="assumeIsPlaying"/> is null for skip/previous/
    /// play-context, where the resulting track is genuinely unknown until asked — those always re-fetch, which
    /// also keeps the cache itself warm for the next Pause/Play.
    /// </summary>
    private async Task PublishPlaybackStateChangedAsync(
        Guid tenantId,
        IMusicProvider provider,
        CancellationToken cancellationToken,
        bool? assumeIsPlaying = null
    )
    {
        TrackInfo? track;
        DateTimeOffset observedAt;

        NowPlayingSnapshot? cached = assumeIsPlaying is not null
            ? _nowPlayingCache.TryGet(tenantId, NowPlayingCacheFreshness)
            : null;

        if (cached is { } snapshot)
        {
            // The track didn't change; only IsPlaying did. Reachable only when assumeIsPlaying is not null —
            // `cached` is computed FROM that same condition above — hence GetValueOrDefault().
            track = TrackAsOfNow(snapshot, assumeIsPlaying.GetValueOrDefault());
            observedAt = DateTimeOffset.UtcNow;
        }
        else
        {
            track = await provider.GetCurrentTrackAsync(tenantId, cancellationToken);
            observedAt = DateTimeOffset.UtcNow;
            if (track is not null)
                _nowPlayingCache.Set(tenantId, track, observedAt);
        }

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
                RequestedBy = RequesterOfPlayingTrack(
                    _queueStore,
                    tenantId.ToString(),
                    track?.TrackUri
                ),
                ShuffleEnabled = track?.ShuffleEnabled ?? false,
                RepeatMode = track?.RepeatMode ?? MusicRepeatMode.Off,
                VolumePercent = track?.VolumePercent ?? 100,
                ObservedAt = observedAt,
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

    /// <inheritdoc />
    public IReadOnlyList<SongRequestQueueSnapshotItem> SnapshotQueue(string broadcasterId)
    {
        FairQueue<SongRequestEntry>? queue = _queueStore.TryGet(broadcasterId);
        return queue is null
            ? []
            : queue
                .GetSnapshot()
                .Take(QueueSnapshotSize)
                .Select(
                    (e, index) =>
                        new SongRequestQueueSnapshotItem(
                            e.Item.TrackName,
                            e.Item.RequestedBy,
                            e.Item.DurationMs / 1000,
                            QueueNumbering.NumberOf(index),
                            e.Item.Code
                        )
                )
                .ToList();
    }

    /// <summary>
    /// A link the channel's sources do not allow (YouTube off) is refused outright, never rerouted to the
    /// active provider. Null when the link is fine or owned by no registered provider.
    /// </summary>
    private async Task<Result?> RefuseIfSourceNotAllowedAsync(
        Guid tenantId,
        MusicConfigDto? config,
        string linkText,
        CancellationToken cancellationToken
    )
    {
        IReadOnlyList<IMusicProvider> usable = await UsableMusicProviders.ForChannelAsync(
            _db,
            _providers,
            tenantId,
            cancellationToken
        );
        IMusicProvider? owner = usable.FirstOrDefault(p => p.OwnsTrackUri(linkText));
        return owner is not null && !IsAllowed(owner.Provider, config)
            ? Result.Failure(
                $"{owner.Provider} song requests are not allowed in this channel.",
                "PROVIDER_NOT_ALLOWED"
            )
            : null;
    }

    private static bool IsSameTrack(string? a, string? b) =>
        string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

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
                "DUPLICATE_TRACK",
                pending.Item.RequestedBy,
                new MusicRequestRefusal(
                    trackInfo.TrackName,
                    trackInfo.Artist,
                    pending.Item.RequestedBy
                )
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
                "DUPLICATE_TRACK",
                errorData: new MusicRequestRefusal(
                    trackInfo.TrackName,
                    trackInfo.Artist,
                    IsPlayingNow: true
                )
            );

        // The provider's OWN queue, not our fair queue's view of it — catches a track the streamer queued
        // by hand from the Spotify app itself, or one our in-memory fair queue lost track of (e.g. after a
        // restart), that the checks above never see. Best-effort by contract (GetQueueAsync never throws;
        // an unanswerable read returns null), so a provider outage falls through to the gates below
        // instead of blocking every request.
        IReadOnlyList<TrackInfo>? providerQueue = await provider.GetQueueAsync(
            tenantId,
            cancellationToken
        );
        if (
            providerQueue is not null
            && providerQueue.Any(t =>
                string.Equals(t.TrackUri, trackInfo.TrackUri, StringComparison.OrdinalIgnoreCase)
            )
        )
            return Result.Failure(
                $"\"{trackInfo.TrackName}\" is already queued up.",
                "DUPLICATE_TRACK",
                errorData: new MusicRequestRefusal(trackInfo.TrackName, trackInfo.Artist)
            );

        return null;
    }

    // The stored history shape is a contract two different readers parse, so its casing is pinned here
    // rather than left to whatever the default happens to be.
    private static readonly System.Text.Json.JsonSerializerOptions HistoryJson = new()
    {
        PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
    };

    /// <summary>
    /// Appends this accepted request to the requester's own history — one row per acceptance, so a repeat is
    /// countable and "most-requested" is a real count rather than a coin toss. Stored on the generic
    /// <c>Record</c> store (the shape the legacy bot used) keyed on the requester's PLATFORM id, which is what
    /// a modiversary notice identifies a mod by.
    ///
    /// <para>Skipped when no viewer is behind the request (dashboard, script, reconciler): a row keyed on
    /// "anonymous" would pool every such request under one fake viewer and skew the counts.</para>
    ///
    /// <para>The resolved title/artist/art ride along, unlike the legacy bot's bare song id — reading a
    /// viewer's top track back then needed a provider round-trip to learn what it even was, and produced
    /// nothing once a track was delisted. Best-effort by design: a failure here must never fail a request the
    /// viewer already had accepted.</para>
    /// </summary>
    private async Task RecordRequestHistoryAsync(
        Guid tenantId,
        TrackInfo trackInfo,
        string? requesterUserId,
        CancellationToken cancellationToken
    )
    {
        if (string.IsNullOrWhiteSpace(requesterUserId))
            return;

        try
        {
            _db.Records.Add(
                new Domain.Platform.Entities.Record
                {
                    BroadcasterId = tenantId,
                    UserId = requesterUserId,
                    RecordType = SongRequestHistory.RecordType,
                    Data = System.Text.Json.JsonSerializer.Serialize(
                        new SongRequestHistory(
                            trackInfo.TrackUri,
                            trackInfo.TrackName,
                            trackInfo.Artist,
                            trackInfo.AlbumArtUrl,
                            trackInfo.Provider
                        ),
                        HistoryJson
                    ),
                }
            );
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Could not record song-request history for {UserId} on {TenantId}",
                requesterUserId,
                tenantId
            );
        }
    }
}
