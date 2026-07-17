// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Moderation.Dtos;

namespace NomNomzBot.Application.Moderation.Services;

/// <summary>
/// The escalation ladder (moderation.md §3.11, J.10/J.11): repeat offenders climb a per-channel
/// punishment ladder inside a decaying offense window. <see cref="ResolveAndRecordAsync"/> is the entry
/// point any filter/automod path calls when a rule defers its punishment to the ladder — the caller
/// applies the returned decision through <c>IModerationService</c>.
/// </summary>
public interface IModerationEscalationService
{
    /// <summary>
    /// Records one offense (the window restarts the tally when <c>OffenseWindowHours</c> elapsed since
    /// it began, else the count increments) and returns the ladder step for the NEW count — clamped to
    /// the highest step. <c>VALIDATION_FAILED</c> when the channel's ladder is disabled or empty.
    /// </summary>
    Task<Result<EscalationDecision>> ResolveAndRecordAsync(
        Guid broadcasterId,
        Guid subjectUserId,
        string subjectTwitchUserId,
        CancellationToken ct = default
    );

    /// <summary>The channel's policy; a channel with no row reads as the disabled DEFAULT ladder
    /// (1 warn → 60s → 600s → 3600s → 86400s → ban, 168h window).</summary>
    Task<Result<ModerationEscalationPolicyDto>> GetPolicyAsync(
        Guid broadcasterId,
        CancellationToken ct = default
    );

    /// <summary>Upserts the policy (whole ladder replaced; steps must be positive and ascending).</summary>
    Task<Result<ModerationEscalationPolicyDto>> UpsertPolicyAsync(
        Guid broadcasterId,
        UpsertEscalationPolicyRequest request,
        CancellationToken ct = default
    );

    /// <summary>Forgiveness — clears the viewer's tally so their next offense starts at rung one.</summary>
    Task<Result> ResetUserAsync(
        Guid broadcasterId,
        Guid subjectUserId,
        CancellationToken ct = default
    );
}
