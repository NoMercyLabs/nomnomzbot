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
        TimeSpan window = TimeSpan.FromHours(policy.OffenseWindowHours);

        // F13/S005: two concurrent offenses against a TRACKED entity's `OffenseCount++` then SaveChangesAsync
        // is a classic read-modify-write — the second SaveChangesAsync overwrites the first's increment and
        // one offense vanishes, so the ladder never reaches its correct rung. Reuse the S004/S004b mechanism
        // this session closed 9/9 elsewhere: an unconditional `ExecuteUpdateAsync` (`OffenseCount + 1`)
        // evaluated against the CURRENT row at write time, never a stale in-memory value, with a
        // DbUpdateException-guarded insert-and-retry for the cold-start (no row yet) case.
        int newCount = await TryAtomicIncrementAsync(broadcasterId, subjectUserId, now, window, ct);
        if (newCount == 0)
        {
            // No row for this (channel, subject) yet. Insert the first-offense row directly; a concurrent
            // caller racing this same cold start loses the unique (BroadcasterId, SubjectUserId) insert and
            // folds its offense into the winner's row via the atomic increment retry below, instead of
            // throwing or silently dropping it.
            ModerationEscalationState inserted = new()
            {
                BroadcasterId = broadcasterId,
                SubjectUserId = subjectUserId,
                SubjectTwitchUserId = subjectTwitchUserId,
                OffenseCount = 1,
                WindowStartedAt = now,
                LastOffenseAt = now,
            };
            db.ModerationEscalationStates.Add(inserted);
            try
            {
                await db.SaveChangesAsync(ct);
                newCount = 1;
            }
            catch (DbUpdateException)
            {
                if (db is DbContext context)
                    context.Entry(inserted).State = EntityState.Detached;
                newCount = await TryAtomicIncrementAsync(
                    broadcasterId,
                    subjectUserId,
                    now,
                    window,
                    ct
                );
                if (newCount == 0)
                    return Result.Failure<EscalationDecision>(
                        "Failed to record the offense after an insert-race retry.",
                        "ESCALATION_STATE_RACE"
                    );
            }
        }

        // The step for the NEW count — the highest configured rung clamps everything above it.
        EscalationLadderStep step =
            ladder.Where(s => s.AtOffense <= newCount).MaxBy(s => s.AtOffense) ?? ladder[0];
        return Result.Success(new EscalationDecision(step.Action, step.TimeoutSeconds, newCount));
    }

    /// <summary>
    /// Atomically advances the tally for an EXISTING row: resets to rung one when the decaying window
    /// lapsed, else increments by one. Both branches are separate `SET ... WHERE ...` statements, mutually
    /// exclusive on the window-lapsed predicate IN THE WHERE CLAUSE (not the SET value — EF Core's
    /// `ExecuteUpdate` cannot translate a conditional expression inside `SetProperty`, only inside `Where`),
    /// each evaluated atomically against the CURRENT row at write time, never a previously-read value, so
    /// concurrent callers never overwrite each other's increment. Returns the row's new
    /// <c>OffenseCount</c> after the update, or 0 if no row exists yet.
    /// </summary>
    private async Task<int> TryAtomicIncrementAsync(
        Guid broadcasterId,
        Guid subjectUserId,
        DateTime now,
        TimeSpan window,
        CancellationToken ct
    )
    {
        // Plain DateTime comparisons only — EF Core's SQLite provider cannot translate `DateTime - DateTime`
        // (TimeSpan) arithmetic in a query. `WindowStartedAt >= cutoff` is exactly `now - WindowStartedAt <=
        // window` restated without a TimeSpan operand.
        DateTime cutoff = now - window;

        int rowsUpdated = await db
            .ModerationEscalationStates.Where(s =>
                s.BroadcasterId == broadcasterId
                && s.SubjectUserId == subjectUserId
                && s.WindowStartedAt >= cutoff
            )
            .ExecuteUpdateAsync(
                setters =>
                    setters
                        .SetProperty(s => s.OffenseCount, s => s.OffenseCount + 1)
                        .SetProperty(s => s.LastOffenseAt, now),
                ct
            );

        if (rowsUpdated == 0)
            rowsUpdated = await db
                .ModerationEscalationStates.Where(s =>
                    s.BroadcasterId == broadcasterId
                    && s.SubjectUserId == subjectUserId
                    && s.WindowStartedAt < cutoff
                )
                .ExecuteUpdateAsync(
                    setters =>
                        setters
                            .SetProperty(s => s.OffenseCount, 1)
                            .SetProperty(s => s.WindowStartedAt, now)
                            .SetProperty(s => s.LastOffenseAt, now),
                    ct
                );

        if (rowsUpdated == 0)
            return 0;

        return await db
            .ModerationEscalationStates.Where(s =>
                s.BroadcasterId == broadcasterId && s.SubjectUserId == subjectUserId
            )
            .Select(s => s.OffenseCount)
            .FirstAsync(ct);
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
            policy is null ? new(false, DefaultLadder, 168, false) : ToDto(policy)
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
            if (step is { Action: "timeout", TimeoutSeconds: not > 0 })
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
            policy = new() { BroadcasterId = broadcasterId };
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
