// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Domain.Moderation.Enums;

/// <summary>
/// What a matched <c>ChatFilter</c> (moderation.md J.6, §2) does. <see cref="Delete"/>/<see cref="Timeout"/>
/// apply a fixed action; <see cref="Hold"/>/<see cref="Flag"/> route the message to review; and
/// <see cref="Escalate"/> defers the punishment to the per-channel escalation ladder (§3.11), which decides
/// warn/timeout/ban by the subject's running offense count. A filter never bans directly — a ban only ever
/// arrives through the ladder.
/// </summary>
public enum ChatFilterAction
{
    Delete,
    Timeout,
    Hold,
    Flag,
    Escalate,
}
