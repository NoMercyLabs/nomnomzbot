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
using NomNomzBot.Domain.Music.Interfaces;

namespace NomNomzBot.Infrastructure.Music;

/// <summary>
/// YouTube music provider stub — the YouTube-provider slice wires the Data API v3 search/resolve and
/// the §3.10 manage surface. YouTube playback rides the browser-source IFrame player by design
/// (music-sr.md §3.5.2), so the transport capabilities (Volume/Seek/Previous/Shuffle/Repeat/
/// TransferDevice) are permanently absent: the YouTube Data API has no playback-transport control,
/// and consumers gate those members off with <c>CAPABILITY_UNSUPPORTED</c>.
/// </summary>
public sealed class YouTubeMusicProvider : IMusicProvider
{
    private const string ProviderName = "youtube";

    private readonly ILogger<YouTubeMusicProvider> _logger;

    public YouTubeMusicProvider(ILogger<YouTubeMusicProvider> logger)
    {
        _logger = logger;
    }

    public string Provider => ProviderName;

    /// <summary>
    /// The §3.5 routing set. The manage flags (<c>Library</c>/<c>Playlists</c>/<c>Subscriptions</c>)
    /// arrive with the YouTube-provider slice that wires videos.rate / playlists.* / subscriptions.*.
    /// </summary>
    public MusicProviderCapabilities Capabilities =>
        MusicProviderCapabilities.Search
        | MusicProviderCapabilities.Queue
        | MusicProviderCapabilities.NowPlaying
        | MusicProviderCapabilities.AcceptsSongRequests;

    public Task PlayAsync(Guid broadcasterId, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("YouTubeMusicProvider.PlayAsync not yet implemented");
        return Task.CompletedTask;
    }

    public Task PauseAsync(Guid broadcasterId, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("YouTubeMusicProvider.PauseAsync not yet implemented");
        return Task.CompletedTask;
    }

    public Task SkipAsync(Guid broadcasterId, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("YouTubeMusicProvider.SkipAsync not yet implemented");
        return Task.CompletedTask;
    }

    public Task PreviousAsync(Guid broadcasterId, CancellationToken cancellationToken = default)
    {
        // No transport control on the YouTube Data API — capability permanently absent; consumers
        // never reach this member through the capability gate.
        _logger.LogDebug("YouTubeMusicProvider.PreviousAsync has no API-side transport");
        return Task.CompletedTask;
    }

    public Task SetVolumeAsync(
        Guid broadcasterId,
        int volumePercent,
        CancellationToken cancellationToken = default
    )
    {
        _logger.LogDebug("YouTubeMusicProvider.SetVolumeAsync has no API-side transport");
        return Task.CompletedTask;
    }

    public Task SeekAsync(
        Guid broadcasterId,
        int positionSeconds,
        CancellationToken cancellationToken = default
    )
    {
        _logger.LogDebug("YouTubeMusicProvider.SeekAsync has no API-side transport");
        return Task.CompletedTask;
    }

    public Task SetShuffleAsync(
        Guid broadcasterId,
        bool enabled,
        CancellationToken cancellationToken = default
    )
    {
        _logger.LogDebug("YouTubeMusicProvider.SetShuffleAsync has no API-side transport");
        return Task.CompletedTask;
    }

    public Task SetRepeatAsync(
        Guid broadcasterId,
        MusicRepeatMode mode,
        CancellationToken cancellationToken = default
    )
    {
        _logger.LogDebug("YouTubeMusicProvider.SetRepeatAsync has no API-side transport");
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<MusicDeviceInfo>> GetDevicesAsync(
        Guid broadcasterId,
        CancellationToken cancellationToken = default
    )
    {
        _logger.LogDebug("YouTubeMusicProvider.GetDevicesAsync has no API-side transport");
        return Task.FromResult<IReadOnlyList<MusicDeviceInfo>>([]);
    }

    public Task TransferPlaybackAsync(
        Guid broadcasterId,
        string deviceId,
        bool play,
        CancellationToken cancellationToken = default
    )
    {
        _logger.LogDebug("YouTubeMusicProvider.TransferPlaybackAsync has no API-side transport");
        return Task.CompletedTask;
    }

    public Task<TrackInfo?> GetCurrentTrackAsync(
        Guid broadcasterId,
        CancellationToken cancellationToken = default
    )
    {
        _logger.LogDebug("YouTubeMusicProvider.GetCurrentTrackAsync not yet implemented");
        return Task.FromResult<TrackInfo?>(null);
    }

    public Task<IReadOnlyList<TrackInfo>> SearchAsync(
        Guid broadcasterId,
        string query,
        int maxResults = 5,
        CancellationToken cancellationToken = default
    )
    {
        _logger.LogDebug("YouTubeMusicProvider.SearchAsync not yet implemented");
        return Task.FromResult<IReadOnlyList<TrackInfo>>([]);
    }

    public Task<TrackInfo?> ResolveTrackAsync(
        Guid broadcasterId,
        string uriOrId,
        CancellationToken cancellationToken = default
    )
    {
        // §3.5: null = not found/unavailable. The YouTube-provider slice wires videos.list here.
        _logger.LogDebug("YouTubeMusicProvider.ResolveTrackAsync not yet implemented");
        return Task.FromResult<TrackInfo?>(null);
    }

    public Task<bool> AddToQueueAsync(
        Guid broadcasterId,
        string trackUri,
        CancellationToken cancellationToken = default
    )
    {
        _logger.LogDebug("YouTubeMusicProvider.AddToQueueAsync not yet implemented");
        return Task.FromResult(false);
    }
}
