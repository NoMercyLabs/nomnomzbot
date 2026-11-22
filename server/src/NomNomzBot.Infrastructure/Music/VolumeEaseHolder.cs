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

namespace NomNomzBot.Infrastructure.Music;

/// <summary>
/// Holds the one running volume ease of each channel. <see cref="MusicService"/> is scoped, so a newer
/// volume change arrives on another instance; this singleton is what lets it stop the ease already running.
/// </summary>
public sealed class VolumeEaseHolder
{
    private readonly Dictionary<Guid, CancellationTokenSource> _running = [];

    /// <summary>Makes <paramref name="ease"/> the channel's running ease and cancels the one it replaces.</summary>
    public void Replace(Guid tenantId, CancellationTokenSource ease)
    {
        CancellationTokenSource? previous;
        lock (_running)
        {
            _running.Remove(tenantId, out previous);
            _running[tenantId] = ease;
        }

        try
        {
            previous?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The earlier ease finished on its own between the swap and the cancel.
        }
    }

    /// <summary>Forgets <paramref name="ease"/> when it is still the channel's running ease.</summary>
    public void End(Guid tenantId, CancellationTokenSource ease)
    {
        lock (_running)
        {
            if (
                _running.TryGetValue(tenantId, out CancellationTokenSource? running)
                && running == ease
            )
                _running.Remove(tenantId);
        }
    }
}
