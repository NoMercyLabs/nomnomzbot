// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Domain.Platform;

namespace NomNomzBot.Domain.Moderation.Entities;

/// <summary>
/// One lockdown window on one platform, persisted (spam-defense.md §L5.1).
///
/// <para>Stored rather than held in memory because a lockdown changes the platform's own room settings
/// and must put every one of them back. A window kept only in process memory loses the prior values on
/// restart, and the room stays tightened with nobody left who knows what to restore.</para>
/// </summary>
public class LockdownWindowRecord : SoftDeletableEntity, ITenantScoped
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid BroadcasterId { get; set; }

    /// <summary>The platform whose room was tightened (<c>twitch</c>, <c>kick</c>, ...).</summary>
    public string Platform { get; set; } = string.Empty;

    /// <summary>Why the room was tightened, in words an operator can read back later.</summary>
    public string Trigger { get; set; } = string.Empty;

    public DateTime StartedAt { get; set; }

    /// <summary>When the window ends by itself. A lockdown always ends by itself.</summary>
    public DateTime ExpiresAt { get; set; }

    /// <summary>Set when a moderator ends the window early.</summary>
    public DateTime? EndedAt { get; set; }

    /// <summary>Stamped only after the real platform undo succeeded for every engaged control.</summary>
    public DateTime? RestoredAt { get; set; }

    /// <summary>
    /// JSON array of the controls this window tightened, each with the value to put back: a serialised
    /// <c>List&lt;EngagedControl&gt;</c> (control name and previous value).
    /// </summary>
    public string EngagedControlsJson { get; set; } = "[]";

    /// <summary>JSON array of requested control names this platform does not offer.</summary>
    public string UnavailableControlsJson { get; set; } = "[]";

    /// <summary>
    /// JSON array of control names the last restore attempt could NOT put back — <c>[]</c> when the
    /// restore was complete. Kept so a partial failure is never silent: a later retry can see exactly
    /// which settings are still tightened.
    /// </summary>
    public string RestorationFailedControlsJson { get; set; } = "[]";
}
