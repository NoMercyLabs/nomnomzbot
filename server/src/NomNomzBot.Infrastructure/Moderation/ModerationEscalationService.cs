// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Moderation.Dtos;
using NomNomzBot.Application.Moderation.Services;
using NomNomzBot.Domain.Moderation.Entities;

namespace NomNomzBot.Infrastructure.Moderation;

/// <summary>
/// The escalation ladder (moderation.md §3.11). The DEFAULT ladder (disabled until a broadcaster turns it
/// on): offense 1 → warn, 2 → 60s, 3 → 600s, 4 → 3600s, 5 → 86400s, 6+ → ban (the highest step clamps);
/// window 168 h. The tally restarts when the window lapses and clears on moderator forgiveness.
/// </summary>
public sealed class ModerationEscalationService(IApplicationDbContext db, TimeProvider clock)
    : IModerationEscalationService
{
    private static readonly IReadOnlyList<EscalationLadderStep> DefaultLadder =
    [
        new(1, "warn", null),
        new(2, "timeout", 60),
        new(3, "timeout", 600),
        new(4, "timeout", 3600),
        new(5, "timeout", 86400),
        new(6, "ban", null),
    ];

    public async Task<Result<EscalationDecision>> ResolveAndRecordAsync(
        Guid broadcasterId,
        Guid subjectUserId,
        string subjectTwitchUserId,
        CancellationToken ct = default
    )
    {
        ModerationEscalationPolicy? policy =
            await db.ModerationEscalationPolicies.FirstOrDefaultAsync(
                p => p.BroadcasterId == broadcasterId,
                ct
            );
        if (policy is not { IsEnabled: true })
            return Result.Failure<EscalationDecision>(
                "The escalation ladder is not enabled for this channel.",
                "VALIDATION_FAILED"
            );
        IReadOnlyList<EscalationLadderStep> ladder = ParseLadder(policy.LadderJson);
        if (ladder.Count == 0)
            return Result.Failure<EscalationDecision>(
                "The escalation ladder has no steps.",
                "VALIDATION_FAILED"
            );

        DateTime now = clock.GetUtcNow().UtcDateTime;
        ModerationEscalationState? state = await db.ModerationEscalationStates.FirstOrDefaultAsync(
            s => s.BroadcasterId == broadcasterId && s.SubjectUserId == subjectUserId,
            ct
        );
        if (state is null)
        {
            state = new ModerationEscalationState
            {
                BroadcasterId = broadcasterId,
                SubjectUserId = subjectUserId,
                SubjectTwitchUserId = subjectTwitchUserId,
                WindowStartedAt = now,
            };
            db.ModerationEscalationStates.Add(state);
        }
        else if (now - state.WindowStartedAt > TimeSpan.FromHours(policy.OffenseWindowHours))
        {
            // The decaying window lapsed — the tally restarts at rung one.
            state.OffenseCount = 0;
            state.WindowStartedAt = now;
        }

        state.OffenseCount++;
        state.LastOffenseAt = now;
        await db.SaveChangesAsync(ct);

        // The step for the NEW count — the highest configured rung clamps everything above it.
        EscalationLadderStep step =
            ladder.Where(s => s.AtOffense <= state.OffenseCount).MaxBy(s => s.AtOffense)
            ?? ladder[0];
        return Result.Success(
            new EscalationDecision(step.Action, step.TimeoutSeconds, state.OffenseCount)
        );
    }

    public async Task<Result<ModerationEscalationPolicyDto>> GetPolicyAsync(
        Guid broadcasterId,
        CancellationToken ct = default
    )
    {
        ModerationEscalationPolicy? policy =
            await db.ModerationEscalationPolicies.FirstOrDefaultAsync(
                p => p.BroadcasterId == broadcasterId,
                ct
            );
        return Result.Success(
            policy is null
                ? new ModerationEscalationPolicyDto(false, DefaultLadder, 168, false)
                : ToDto(policy)
        );
    }

    public async Task<Result<ModerationEscalationPolicyDto>> UpsertPolicyAsync(
        Guid broadcasterId,
        UpsertEscalationPolicyRequest request,
        CancellationToken ct = default
    )
    {
        if (request.OffenseWindowHours <= 0)
            return Result.Failure<ModerationEscalationPolicyDto>(
                "The offense window must be positive.",
                "VALIDATION_FAILED"
            );
        if (request.Ladder.Count == 0)
            return Result.Failure<ModerationEscalationPolicyDto>(
                "The ladder needs at least one step.",
                "VALIDATION_FAILED"
            );
        int previous = 0;
        foreach (EscalationLadderStep step in request.Ladder)
        {
            if (step.AtOffense <= previous)
                return Result.Failure<ModerationEscalationPolicyDto>(
                    "Ladder steps must be strictly ascending by offense number.",
                    "VALIDATION_FAILED"
                );
            if (step.Action is not ("warn" or "timeout" or "ban"))
                return Result.Failure<ModerationEscalationPolicyDto>(
                    $"Unknown ladder action '{step.Action}' — warn, timeout, or ban.",
                    "VALIDATION_FAILED"
                );
            if (step.Action == "timeout" && step.TimeoutSeconds is not > 0)
                return Result.Failure<ModerationEscalationPolicyDto>(
                    "A timeout step needs a positive duration.",
                    "VALIDATION_FAILED"
                );
            previous = step.AtOffense;
        }

        ModerationEscalationPolicy? policy =
            await db.ModerationEscalationPolicies.FirstOrDefaultAsync(
                p => p.BroadcasterId == broadcasterId,
                ct
            );
        if (policy is null)
        {
            policy = new ModerationEscalationPolicy { BroadcasterId = broadcasterId };
            db.ModerationEscalationPolicies.Add(policy);
        }
        policy.IsEnabled = request.IsEnabled;
        policy.LadderJson = JsonSerializer.Serialize(request.Ladder);
        policy.OffenseWindowHours = request.OffenseWindowHours;
        policy.CountAutoModViolations = request.CountAutoModViolations;
        await db.SaveChangesAsync(ct);
        return Result.Success(ToDto(policy));
    }

    public async Task<Result> ResetUserAsync(
        Guid broadcasterId,
        Guid subjectUserId,
        CancellationToken ct = default
    )
    {
        ModerationEscalationState? state = await db.ModerationEscalationStates.FirstOrDefaultAsync(
            s => s.BroadcasterId == broadcasterId && s.SubjectUserId == subjectUserId,
            ct
        );
        if (state is null)
            return Result.Success(); // already at rung zero — forgiveness is idempotent

        db.ModerationEscalationStates.Remove(state);
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    private static IReadOnlyList<EscalationLadderStep> ParseLadder(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<List<EscalationLadderStep>>(json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static ModerationEscalationPolicyDto ToDto(ModerationEscalationPolicy policy) =>
        new(
            policy.IsEnabled,
            ParseLadder(policy.LadderJson),
            policy.OffenseWindowHours,
            policy.CountAutoModViolations
        );
}
