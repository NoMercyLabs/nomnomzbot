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

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Music.Services;

namespace NomNomzBot.Infrastructure.Music.Playback;

/// <summary>Tells the director whether the channel has turned YouTube off.</summary>
public interface IYouTubeOffSwitch
{
    Task<bool> IsOffAsync(Guid broadcasterId, CancellationToken ct = default);
}

/// <summary>
/// The singleton director's way to the scoped music config. A failed read counts as "not off": a database
/// hiccup must never stop the music or delete a request.
/// </summary>
public sealed class YouTubeOffSwitch(IServiceScopeFactory scopes, ILogger<YouTubeOffSwitch> logger)
    : IYouTubeOffSwitch
{
    public async Task<bool> IsOffAsync(Guid broadcasterId, CancellationToken ct = default)
    {
        try
        {
            await using AsyncServiceScope scope = scopes.CreateAsyncScope();
            IMusicConfigService config =
                scope.ServiceProvider.GetRequiredService<IMusicConfigService>();

            return await config.IsYouTubeSwitchedOffAsync(broadcasterId.ToString(), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "YouTube off switch read failed for channel {C}", broadcasterId);

            return false;
        }
    }
}
