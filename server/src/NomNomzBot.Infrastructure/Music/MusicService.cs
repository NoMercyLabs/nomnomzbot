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
        IMusicProvider? provider = await GetActiveProviderAsync(broadcasterId, cancellationToken);
        if (provider is null)
            return [];

        IReadOnlyList<TrackInfo> results = await provider.SearchAsync(
            broadcasterId,
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
        IMusicProvider? provider = await GetActiveProviderAsync(broadcasterId, cancellationToken);
        if (provider is null)
            return false;

        await provider.PlayAsync(broadcasterId, cancellationToken);
        await PublishPlaybackStateChangedAsync(broadcasterId, provider, cancellationToken);
        return true;
    }

    public async Task<bool> PauseAsync(
        string broadcasterId,
        CancellationToken cancellationToken = default
    )
    {
        IMusicProvider? provider = await GetActiveProviderAsync(broadcasterId, cancellationToken);
        if (provider is null)
            return false;

        await provider.PauseAsync(broadcasterId, cancellationToken);
        await PublishPlaybackStateChangedAsync(broadcasterId, provider, cancellationToken);
        return true;
    }

    public async Task<bool> SkipAsync(
        string broadcasterId,
        CancellationToken cancellationToken = default
    )
    {
        IMusicProvider? provider = await GetActiveProviderAsync(broadcasterId, cancellationToken);
        if (provider is null)
            return false;

        // Dequeue next from fair queue and add to provider queue
        SongRequestEntry? next = DequeueNext(broadcasterId);
        if (next is not null)
        {
            await provider.AddToQueueAsync(broadcasterId, next.TrackUri, cancellationToken);
        }

        await provider.SkipAsync(broadcasterId, cancellationToken);
        await PublishPlaybackStateChangedAsync(broadcasterId, provider, cancellationToken);
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
        IMusicProvider? provider = await GetActiveProviderAsync(broadcasterId, cancellationToken);
        if (provider is null)
            return false;

        // Look up track info for the queue display
        IReadOnlyList<TrackInfo> track = await provider.SearchAsync(
            broadcasterId,
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
            await provider.AddToQueueAsync(broadcasterId, trackUri, cancellationToken);
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
        // Volume control is Spotify-specific; try Spotify provider first
        SpotifyMusicProvider? spotifyProvider = _providers
            .OfType<SpotifyMusicProvider>()
            .FirstOrDefault();
        if (spotifyProvider is null)
            return false;

        // Direct Spotify volume — not in IMusicProvider interface, logged only
        _logger.LogDebug(
            "SetVolumeAsync({Volume}) called for {BroadcasterId}",
            volume,
            broadcasterId
        );
        return await Task.FromResult(false);
    }

    public async Task<NowPlaying?> GetNowPlayingAsync(
        string broadcasterId,
        CancellationToken cancellationToken = default
    )
    {
        IMusicProvider? provider = await GetActiveProviderAsync(broadcasterId, cancellationToken);
        if (provider is null)
            return null;

        TrackInfo? track = await provider.GetCurrentTrackAsync(broadcasterId, cancellationToken);
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

    private async Task<IMusicProvider?> GetActiveProviderAsync(
        string broadcasterId,
        CancellationToken cancellationToken
    )
    {
        // Service.BroadcasterId is the tenant Guid; the service receives it as a string.
        if (!Guid.TryParse(broadcasterId, out Guid tenantId))
            return null;

        // Look up which services are connected for this broadcaster
        List<string> services = await _db
            .Services.Where(s => s.BroadcasterId == tenantId && s.Enabled && s.AccessToken != null)
            .Select(s => s.Name)
            .ToListAsync(cancellationToken);

        // Priority: Spotify > YouTube
        if (services.Contains("spotify"))
        {
            SpotifyMusicProvider? spotify = _providers
                .OfType<SpotifyMusicProvider>()
                .FirstOrDefault();
            if (spotify is not null)
                return spotify;
        }

        if (services.Contains("youtube"))
        {
            YouTubeMusicProvider? youtube = _providers
                .OfType<YouTubeMusicProvider>()
                .FirstOrDefault();
            if (youtube is not null)
                return youtube;
        }

        _logger.LogDebug("No active music provider for broadcaster {BroadcasterId}", broadcasterId);
        return null;
    }

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

    // ── Remote controls (delegate to IMusicRemoteProvider when available) ────────

    public async Task<bool> SeekAsync(
        string broadcasterId,
        int positionMs,
        CancellationToken cancellationToken = default
    )
    {
        IMusicRemoteProvider? remote = await GetRemoteProviderAsync(
            broadcasterId,
            cancellationToken
        );
        if (remote is null)
            return false;
        await remote.SeekAsync(broadcasterId, positionMs, cancellationToken);
        return true;
    }

    public async Task<bool> SetShuffleAsync(
        string broadcasterId,
        bool enabled,
        CancellationToken cancellationToken = default
    )
    {
        IMusicRemoteProvider? remote = await GetRemoteProviderAsync(
            broadcasterId,
            cancellationToken
        );
        if (remote is null)
            return false;
        await remote.SetShuffleAsync(broadcasterId, enabled, cancellationToken);
        return true;
    }

    public async Task<bool> SetRepeatAsync(
        string broadcasterId,
        string mode,
        CancellationToken cancellationToken = default
    )
    {
        IMusicRemoteProvider? remote = await GetRemoteProviderAsync(
            broadcasterId,
            cancellationToken
        );
        if (remote is null)
            return false;
        await remote.SetRepeatAsync(broadcasterId, mode, cancellationToken);
        return true;
    }

    public async Task<bool> TransferPlaybackAsync(
        string broadcasterId,
        string deviceId,
        bool play = false,
        CancellationToken cancellationToken = default
    )
    {
        IMusicRemoteProvider? remote = await GetRemoteProviderAsync(
            broadcasterId,
            cancellationToken
        );
        if (remote is null)
            return false;
        await remote.TransferPlaybackAsync(broadcasterId, deviceId, play, cancellationToken);
        return true;
    }

    public async Task<IReadOnlyList<MusicDeviceDto>> GetDevicesAsync(
        string broadcasterId,
        CancellationToken cancellationToken = default
    )
    {
        IMusicRemoteProvider? remote = await GetRemoteProviderAsync(
            broadcasterId,
            cancellationToken
        );
        if (remote is null)
            return [];
        IReadOnlyList<MusicDevice> devices = await remote.GetDevicesAsync(
            broadcasterId,
            cancellationToken
        );
        return devices
            .Select(d => new MusicDeviceDto(d.Id, d.Name, d.Type, d.IsActive, d.VolumePercent))
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
        IMusicRemoteProvider? remote = await GetRemoteProviderAsync(
            broadcasterId,
            cancellationToken
        );
        if (remote is null)
            return [];
        IReadOnlyList<MusicPlaylist> playlists = await remote.GetPlaylistsAsync(
            broadcasterId,
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
        IMusicRemoteProvider? remote = await GetRemoteProviderAsync(
            broadcasterId,
            cancellationToken
        );
        if (remote is null)
            return false;
        await remote.PlayContextAsync(broadcasterId, contextUri, cancellationToken);

        // Every current IMusicRemoteProvider implementation (Spotify) is also an IMusicProvider, so the
        // current-track read used to publish the fresh state is available on the same instance. Guarded
        // rather than assumed, in case a future remote-only provider does not also implement IMusicProvider.
        if (remote is IMusicProvider musicProvider)
            await PublishPlaybackStateChangedAsync(broadcasterId, musicProvider, cancellationToken);

        return true;
    }

    /// <summary>
    /// Publishes <see cref="PlaybackStateChangedEvent"/> right after a successful mutation (play/pause/skip/
    /// play-context) so the dashboard + overlay update instantly instead of waiting for the next
    /// <c>MusicStatePollingService</c> tick. Re-reads the provider's current track rather than guessing the new
    /// state, since e.g. a skip's next track is only known to the provider.
    /// </summary>
    private async Task PublishPlaybackStateChangedAsync(
        string broadcasterId,
        IMusicProvider provider,
        CancellationToken cancellationToken
    )
    {
        if (!Guid.TryParse(broadcasterId, out Guid tenantId))
            return;

        TrackInfo? track = await provider.GetCurrentTrackAsync(broadcasterId, cancellationToken);

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

    private async Task<IMusicRemoteProvider?> GetRemoteProviderAsync(
        string broadcasterId,
        CancellationToken cancellationToken
    )
    {
        IMusicProvider? provider = await GetActiveProviderAsync(broadcasterId, cancellationToken);
        return provider as IMusicRemoteProvider;
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
