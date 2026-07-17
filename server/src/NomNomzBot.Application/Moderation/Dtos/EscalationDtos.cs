// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Application.Moderation.Dtos;

/// <summary>One rung of the ladder (moderation.md §3.11). <c>Action</c>: warn | timeout | ban.</summary>
public sealed record EscalationLadderStep(int AtOffense, string Action, int? TimeoutSeconds);

/// <summary>What the ladder decided for THIS offense — the caller applies it via IModerationService.</summary>
public sealed record EscalationDecision(string Action, int? TimeoutSeconds, int OffenseCount);

/// <summary>The channel's escalation policy (J.10).</summary>
public sealed record ModerationEscalationPolicyDto(
    bool IsEnabled,
    IReadOnlyList<EscalationLadderStep> Ladder,
    int OffenseWindowHours,
    bool CountAutoModViolations
);

/// <summary>Full-policy upsert — the ladder is replaced whole, never patched.</summary>
public sealed record UpsertEscalationPolicyRequest(
    bool IsEnabled,
    IReadOnlyList<EscalationLadderStep> Ladder,
    int OffenseWindowHours,
    bool CountAutoModViolations
);
