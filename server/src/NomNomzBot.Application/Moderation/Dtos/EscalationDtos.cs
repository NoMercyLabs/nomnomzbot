// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Domain.Moderation.Entities;

namespace NomNomzBot.Application.Moderation.Dtos;

/// <summary>One rung of the ladder (moderation.md §3.11). <c>Action</c>: warn | timeout | ban.</summary>
public sealed record EscalationLadderStep(int AtOffense, string Action, int? TimeoutSeconds);

/// <summary>What the ladder decided for THIS offense — the caller applies it via IModerationService.</summary>
public sealed record EscalationDecision(string Action, int? TimeoutSeconds, int OffenseCount);

// DefaultTimeoutSeconds: the length one click on a chat line's Timeout icon uses (1..1209600; 600 unset).
/// <summary>The channel's escalation policy (J.10).</summary>
public sealed record ModerationEscalationPolicyDto(
    bool IsEnabled,
    IReadOnlyList<EscalationLadderStep> Ladder,
    int OffenseWindowHours,
    bool CountAutoModViolations,
    int DefaultTimeoutSeconds,
    EscalationPolicyDefaultsDto Defaults,
    EscalationPolicyLimitsDto Limits
);

/// <summary>The values a channel with no saved policy resolves to, for "reset to default" and hints.</summary>
public sealed record EscalationPolicyDefaultsDto(
    bool IsEnabled,
    IReadOnlyList<EscalationLadderStep> Ladder,
    int OffenseWindowHours,
    bool CountAutoModViolations,
    int DefaultTimeoutSeconds
)
{
    public static readonly EscalationPolicyDefaultsDto Current = new(
        EscalationPolicyDefaults.IsEnabled,
        [
            .. EscalationPolicyDefaults.Ladder.Select(s => new EscalationLadderStep(
                s.AtOffense,
                s.Action,
                s.TimeoutSeconds
            )),
        ],
        EscalationPolicyDefaults.OffenseWindowHours,
        EscalationPolicyDefaults.CountAutoModViolations,
        EscalationPolicyDefaults.DefaultTimeoutSeconds
    );
}

/// <summary>The ranges a save must keep (the same numbers the service enforces).</summary>
public sealed record EscalationPolicyLimitsDto(
    int MinOffenseWindowHours,
    int MaxOffenseWindowHours,
    int MinTimeoutSeconds,
    int MaxTimeoutSeconds,
    int MinLadderSteps,
    int MaxLadderSteps
)
{
    public static readonly EscalationPolicyLimitsDto Current = new(
        1,
        EscalationPolicyDefaults.MaxOffenseWindowHours,
        1,
        EscalationPolicyDefaults.MaxTimeoutSeconds,
        1,
        EscalationPolicyDefaults.MaxLadderSteps
    );
}

/// <summary>Full-policy upsert — the ladder is replaced whole, never patched.</summary>
public sealed record UpsertEscalationPolicyRequest(
    bool IsEnabled,
    IReadOnlyList<EscalationLadderStep> Ladder,
    int OffenseWindowHours,
    bool CountAutoModViolations,
    int DefaultTimeoutSeconds = EscalationPolicyDefaults.DefaultTimeoutSeconds
);
