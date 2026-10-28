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

using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Moderation.Dtos;
using NomNomzBot.Domain.Moderation.SpamDefense;

namespace NomNomzBot.Application.Moderation.Services;

/// <summary>
/// Runs hate-raid lockdown windows (spam-defense.md §L5.1, SD0, SD12): tighten the platform's own room
/// settings, remember what each one was, and put every one back.
///
/// <para>Lockdown acts on the room, never on a person: nothing here bans, times out or blocks.</para>
/// </summary>
public interface ILockdownService
{
    /// <summary>
    /// Tighten the room on <paramref name="platform"/>. When a window is already active for this
    /// broadcaster and platform it is extended (within the policy ceiling) instead of stacked.
    /// A control that cannot be applied is recorded in the result and the rest still apply.
    /// </summary>
    Task<Result<LockdownWindowStatus>> EngageAsync(
        Guid broadcasterId,
        string platform,
        string trigger,
        IReadOnlyCollection<LockdownControl> requested,
        CancellationToken ct = default
    );

    /// <summary>End the active window now and put the room back. One click, at any point.</summary>
    Task<Result<LockdownWindowStatus>> EndAsync(
        Guid broadcasterId,
        string platform,
        CancellationToken ct = default
    );

    /// <summary>
    /// Every window of this broadcaster not yet fully restored: active ones, and expired or ended ones whose
    /// restore left a control tightened. Empty when the room is back to normal.
    /// </summary>
    Task<IReadOnlyList<LockdownWindowStatus>> GetActiveAsync(
        Guid broadcasterId,
        CancellationToken ct = default
    );

    /// <summary>
    /// Restore every window that has expired, was ended, or whose last restore left a control behind.
    /// Called by the sweep; safe to call at any time. Returns how many windows are now fully restored.
    /// </summary>
    Task<int> RestoreDueAsync(CancellationToken ct = default);
}
