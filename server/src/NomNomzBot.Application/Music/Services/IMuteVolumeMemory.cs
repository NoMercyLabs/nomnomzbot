// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
