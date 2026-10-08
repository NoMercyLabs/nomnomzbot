// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Application.Moderation.Services;

/// <summary>What the ladder did with one automatic-moderation violation.</summary>
/// <param name="Handled">The ladder took the offense. The caller must NOT also run its own timeout or ban.</param>
/// <param name="Action">The ladder step that applied (<c>warn</c>, <c>timeout</c>, <c>ban</c>), when handled.</param>
/// <param name="Applied">The platform accepted the action. False when the ladder took the offense but the call failed.</param>
public sealed record ViolationEscalationOutcome(bool Handled, string? Action, bool Applied)
{
    /// <summary>The ladder is not in play: the caller keeps its own action.</summary>
    public static readonly ViolationEscalationOutcome NotHandled = new(false, null, false);
}

/// <summary>
/// Hands an automatic-moderation violation (an AutoMod timeout/ban rule, a spam-defence escalation) to the
/// channel's escalation ladder, when the channel asked for that: the ladder is enabled AND
/// <c>CountAutoModViolations</c> is on. The ladder's decision then REPLACES the rule's own punishment, so
/// nobody is punished twice for one message.
/// </summary>
public interface IViolationEscalationService
{
    /// <summary>
    /// Records the offense and applies the ladder step through <see cref="IModerationService"/> as the
    /// channel owner. Returns <see cref="ViolationEscalationOutcome.NotHandled"/>, with nothing recorded
    /// and nothing sent, when the setting or the ladder is off or the channel has no resolvable owner.
    /// </summary>
    Task<ViolationEscalationOutcome> TryEscalateAsync(
        Guid broadcasterId,
        string platformUserId,
        string login,
        string displayName,
        string reason,
        CancellationToken ct = default
    );
}
