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

namespace NomNomzBot.Application.Music.Services;

/// <summary>
/// A short-lived per-channel memory of the volume immediately before a mute, so unmuting restores the
/// real prior level instead of resetting to a fixed one. A miss (never muted, or the memory expired)
/// is a normal "nothing to restore" state, not a failure — the caller falls back to a configured level.
/// </summary>
public interface IMuteVolumeMemory
{
    /// <summary>Records the volume observed right before muting.</summary>
    Task RememberAsync(Guid broadcasterId, int volumePercent, CancellationToken ct = default);

    /// <summary>The volume to restore, or null if there is nothing remembered for this channel.</summary>
    Task<int?> GetAsync(Guid broadcasterId, CancellationToken ct = default);
}
