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
/// YouTube Music provider stub (Phase 2 implementation).
/// Requires YouTube Data API v3 OAuth 2.0 integration.
/// </summary>
public sealed class YouTubeMusicProvider : IMusicProvider
{
    private const string ProviderName = "youtube";

    private readonly ILogger<YouTubeMusicProvider> _logger;

    public YouTubeMusicProvider(ILogger<YouTubeMusicProvider> logger)
    {
        _logger = logger;
    }

    public Task PlayAsync(string broadcasterId, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("YouTubeMusicProvider.PlayAsync not yet implemented");
        return Task.CompletedTask;
    }

    public Task PauseAsync(string broadcasterId, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("YouTubeMusicProvider.PauseAsync not yet implemented");
        return Task.CompletedTask;
    }

    public Task SkipAsync(string broadcasterId, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("YouTubeMusicProvider.SkipAsync not yet implemented");
        return Task.CompletedTask;
    }

    public Task<TrackInfo?> GetCurrentTrackAsync(
        string broadcasterId,
        CancellationToken cancellationToken = default
    )
    {
        _logger.LogDebug("YouTubeMusicProvider.GetCurrentTrackAsync not yet implemented");
        return Task.FromResult<TrackInfo?>(null);
    }

    public Task<IReadOnlyList<TrackInfo>> SearchAsync(
        string broadcasterId,
        string query,
        int maxResults = 5,
        CancellationToken cancellationToken = default
    )
    {
        _logger.LogDebug("YouTubeMusicProvider.SearchAsync not yet implemented");
        return Task.FromResult<IReadOnlyList<TrackInfo>>([]);
    }

    public Task<bool> AddToQueueAsync(
        string broadcasterId,
        string trackUri,
        CancellationToken cancellationToken = default
    )
    {
        _logger.LogDebug("YouTubeMusicProvider.AddToQueueAsync not yet implemented");
        return Task.FromResult(false);
    }
}
