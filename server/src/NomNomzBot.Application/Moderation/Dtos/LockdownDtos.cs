// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Domain.Moderation.SpamDefense;

namespace NomNomzBot.Application.Moderation.Dtos;

/// <summary>
/// One lockdown window as the dashboard reads it (spam-defense.md §L5.1). Every list names controls,
/// never people: lockdown never acts on a person.
/// </summary>
/// <param name="Id">Window id.</param>
/// <param name="Platform">Platform whose room was tightened.</param>
/// <param name="Trigger">Why, in words an operator can read back.</param>
/// <param name="StartedAt">When the window opened.</param>
/// <param name="ExpiresAt">When it ends by itself (after any extension).</param>
/// <param name="EndedAt">Set when a moderator ended it early.</param>
/// <param name="RestoredAt">Set only after every engaged control was really put back.</param>
/// <param name="Engaged">Controls this window tightened.</param>
/// <param name="Unavailable">Requested controls this platform does not offer here.</param>
/// <param name="ApplyFailed">Controls the platform refused: NOT in force.</param>
/// <param name="RestorationFailed">Controls the last restore could not put back: still tightened.</param>
public sealed record LockdownWindowStatus(
    Guid Id,
    string Platform,
    string Trigger,
    DateTimeOffset StartedAt,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? EndedAt,
    DateTimeOffset? RestoredAt,
    IReadOnlyList<LockdownControl> Engaged,
    IReadOnlyList<LockdownControl> Unavailable,
    IReadOnlyList<LockdownControl> ApplyFailed,
    IReadOnlyList<LockdownControl> RestorationFailed
);

/// <summary>An operator tightening the room by hand (spam-defense.md §L5.1).</summary>
/// <param name="Platform">Platform to tighten.</param>
/// <param name="Reason">Why, in the operator's words. Stored on the window as <c>manual: &lt;reason&gt;</c>.</param>
/// <param name="Controls">The controls to engage. At least one.</param>
public sealed record EngageLockdownRequest(
    string Platform,
    string Reason,
    IReadOnlyList<LockdownControl> Controls
);

/// <summary>An operator ending the active window on one platform now.</summary>
/// <param name="Platform">Platform whose window to end.</param>
public sealed record EndLockdownRequest(string Platform);
