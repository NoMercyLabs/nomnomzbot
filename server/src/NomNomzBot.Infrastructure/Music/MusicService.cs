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
using NomNomzBot.Application.Music.Services;
using NomNomzBot.Domain.Music.Events;
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
public sealed class MusicService : IMusicService
{
    private readonly IEnumerable<IMusicProvider> _providers;
    private readonly IApplicationDbContext _db;
    private readonly IEventBus _eventBus;
    private readonly ILogger<MusicService> _logger;

    // Per-channel song request queues (channelId → fair queue)
    private readonly Dictionary<string, FairQueue<SongRequestEntry>> _queues = new();
    private readonly Lock _queueLock = new();

    public MusicService(
        IEnumerable<IMusicProvider> providers,
        IApplicationDbContext db,
        IEventBus eventBus,
        ILogger<MusicService> logger
    )
    {
        _providers = providers;
        _db = db;
        _eventBus = eventBus;
        _logger = logger;
    }

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

        IReadOnlyList<TrackInfo> results = await provider.SearchAsync(
            tenantId,
            query,
            maxResults,
            cancellationToken
        );

        return results
            .Select(t => new MusicTrack(
                t.TrackUri,
                t.TrackName,
                t.Artist,
                t.Album,
                t.AlbumArtUrl,
                t.DurationMs,
                t.Provider
            ))
            .ToList();
    }

    public async Task<bool> PlayAsync(
        string broadcasterId,
        CancellationToken cancellationToken = default
    )
    {
        if (!Guid.TryParse(broadcasterId, out Guid tenantId))
            return false;

        IMusicProvider? provider = await GetActiveProviderAsync(tenantId, cancellationToken);
        if (provider is null || !HasCapability(provider, MusicProviderCapabilities.PlaybackControl))
            return false;

        await provider.PlayAsync(tenantId, cancellationToken);
        await PublishPlaybackStateChangedAsync(tenantId, provider, cancellationToken);
        return true;
    }

    public async Task<bool> PauseAsync(
        string broadcasterId,
        CancellationToken cancellationToken = default
    )
    {
        if (!Guid.TryParse(broadcasterId, out Guid tenantId))
            return false;

        IMusicProvider? provider = await GetActiveProviderAsync(tenantId, cancellationToken);
        if (provider is null || !HasCapability(provider, MusicProviderCapabilities.PlaybackControl))
            return false;

        await provider.PauseAsync(tenantId, cancellationToken);
        await PublishPlaybackStateChangedAsync(tenantId, provider, cancellationToken);
        return true;
    }

    public async Task<bool> SkipAsync(
        string broadcasterId,
        CancellationToken cancellationToken = default
    )
    {
        if (!Guid.TryParse(broadcasterId, out Guid tenantId))
            return false;

        IMusicProvider? provider = await GetActiveProviderAsync(tenantId, cancellationToken);
        if (provider is null || !HasCapability(provider, MusicProviderCapabilities.Skip))
            return false;

        // Dequeue next from fair queue and add to provider queue
        SongRequestEntry? next = DequeueNext(broadcasterId);
        if (next is not null)
        {
            await provider.AddToQueueAsync(tenantId, next.TrackUri, cancellationToken);
        }

        await provider.SkipAsync(tenantId, cancellationToken);
        await PublishPlaybackStateChangedAsync(tenantId, provider, cancellationToken);
        return true;
    }

    public async Task<MusicQueue> GetQueueAsync(
        string broadcasterId,
        CancellationToken cancellationToken = default
    )
    {
        NowPlaying? nowPlaying = await GetNowPlayingAsync(broadcasterId, cancellationToken);

        FairQueue<SongRequestEntry>? queue;
        lock (_queueLock)
        {
            _queues.TryGetValue(broadcasterId, out queue);
        }

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

    public async Task<bool> AddToQueueAsync(
        string broadcasterId,
        string trackUri,
        string? requestedBy = null,
        CancellationToken cancellationToken = default
    )
    {
        if (!Guid.TryParse(broadcasterId, out Guid tenantId))
            return false;

        IMusicProvider? provider = await GetActiveProviderAsync(tenantId, cancellationToken);
        if (provider is null)
            return false;

        // Look up track info for the queue display
        IReadOnlyList<TrackInfo> track = await provider.SearchAsync(
            tenantId,
            trackUri,
            1,
            cancellationToken
        );
        TrackInfo trackInfo =
            track.FirstOrDefault(t => t.TrackUri == trackUri)
            ?? new TrackInfo
            {
                TrackName = trackUri,
                Artist = "Unknown",
                Album = string.Empty,
                TrackUri = trackUri,
                Provider = "unknown",
            };

        SongRequestEntry entry = new(
            trackUri,
            trackInfo.TrackName,
            trackInfo.Artist,
            trackInfo.AlbumArtUrl,
            trackInfo.DurationMs,
            requestedBy ?? "anonymous"
        );

        // Add to fair queue
        FairQueue<SongRequestEntry> queue;
        lock (_queueLock)
        {
            if (!_queues.TryGetValue(broadcasterId, out queue!))
            {
                queue = new();
                _queues[broadcasterId] = queue;
            }
        }

        queue.Enqueue(requestedBy ?? "anonymous", entry);

        // If nothing is in the provider's queue, add immediately
        int queueSize = queue.Count;
        if (queueSize <= 1)
        {
            await provider.AddToQueueAsync(tenantId, trackUri, cancellationToken);
        }

        _logger.LogInformation(
            "Queued track '{Track}' for {BroadcasterId} (requested by {RequestedBy})",
            trackInfo.TrackName,
            broadcasterId,
            requestedBy
        );

        return true;
    }

    public async Task<bool> SetVolumeAsync(
        string broadcasterId,
        int volume,
        CancellationToken cancellationToken = default
    )
    {
        if (!Guid.TryParse(broadcasterId, out Guid tenantId))
            return false;

        IMusicProvider? provider = await GetActiveProviderAsync(tenantId, cancellationToken);
        if (provider is null || !HasCapability(provider, MusicProviderCapabilities.Volume))
            return false;

        // §3.5 defines the Volume capability but no provider declares it yet — the
        // Spotify-completeness slice wires PUT /me/player/volume and adds the SetVolume member
        // alongside the flag. Until then this gate fails closed above; reaching here means a
        // provider declared Volume without the seam having a member to call.
        _logger.LogWarning(
            "Provider '{Provider}' declares Volume but the provider seam has no SetVolume member yet",
            provider.Provider
        );
        return false;
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
            100,
            null,
            track.Provider
        );
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

    public Task<bool> RemoveFromQueueAsync(
        string broadcasterId,
        int position,
        CancellationToken cancellationToken = default
    )
    {
        lock (_queueLock)
        {
            if (!_queues.TryGetValue(broadcasterId, out FairQueue<SongRequestEntry>? queue))
                return Task.FromResult(false);

            return Task.FromResult(queue.RemoveAt(position));
        }
    }

    // ── Remote controls (capability-gated §3.5 members) ─────────────────────────

    public async Task<bool> SeekAsync(
        string broadcasterId,
        int positionMs,
        CancellationToken cancellationToken = default
    )
    {
        if (positionMs < 0 || !Guid.TryParse(broadcasterId, out Guid tenantId))
            return false;

        IMusicProvider? provider = await GetActiveProviderAsync(tenantId, cancellationToken);
        if (provider is null || !HasCapability(provider, MusicProviderCapabilities.Seek))
            return false;

        // The §3.5 seam speaks whole seconds; the legacy wire contract still carries milliseconds.
        await provider.SeekAsync(tenantId, positionMs / 1000, cancellationToken);
        return true;
    }

    public async Task<bool> SetShuffleAsync(
        string broadcasterId,
        bool enabled,
        CancellationToken cancellationToken = default
    )
    {
        if (!Guid.TryParse(broadcasterId, out Guid tenantId))
            return false;

        IMusicProvider? provider = await GetActiveProviderAsync(tenantId, cancellationToken);
        if (provider is null || !HasCapability(provider, MusicProviderCapabilities.Shuffle))
            return false;

        await provider.SetShuffleAsync(tenantId, enabled, cancellationToken);
        return true;
    }

    public async Task<bool> SetRepeatAsync(
        string broadcasterId,
        string mode,
        CancellationToken cancellationToken = default
    )
    {
        if (!Guid.TryParse(broadcasterId, out Guid tenantId))
            return false;

        if (!Enum.TryParse(mode, ignoreCase: true, out MusicRepeatMode repeatMode))
            return false;

        IMusicProvider? provider = await GetActiveProviderAsync(tenantId, cancellationToken);
        if (provider is null || !HasCapability(provider, MusicProviderCapabilities.Repeat))
            return false;

        await provider.SetRepeatAsync(tenantId, repeatMode, cancellationToken);
        return true;
    }

    public async Task<bool> TransferPlaybackAsync(
        string broadcasterId,
        string deviceId,
        bool play = false,
        CancellationToken cancellationToken = default
    )
    {
        if (!Guid.TryParse(broadcasterId, out Guid tenantId))
            return false;

        IMusicProvider? provider = await GetActiveProviderAsync(tenantId, cancellationToken);
        if (provider is null || !HasCapability(provider, MusicProviderCapabilities.TransferDevice))
            return false;

        await provider.TransferPlaybackAsync(tenantId, deviceId, play, cancellationToken);
        return true;
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

        await remote.PlayContextAsync(tenantId, contextUri, cancellationToken);
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
            },
            cancellationToken
        );
    }

    private SongRequestEntry? DequeueNext(string broadcasterId)
    {
        lock (_queueLock)
        {
            return _queues.TryGetValue(broadcasterId, out FairQueue<SongRequestEntry>? queue)
                ? queue.Dequeue()
                : null;
        }
    }
}

/// <summary>An item in the per-channel song request queue.</summary>
internal sealed record SongRequestEntry(
    string TrackUri,
    string TrackName,
    string Artist,
    string? ImageUrl,
    int DurationMs,
    string RequestedBy
);
