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
        // A timeout rung with no length of its own uses the channel's default timeout, never zero.
        int? timeoutSeconds = step is { Action: "timeout", TimeoutSeconds: null or <= 0 }
            ? policy.DefaultTimeoutSeconds
            : step.TimeoutSeconds;
        return Result.Success(new EscalationDecision(step.Action, timeoutSeconds, newCount));
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
            policy is null
                ? new(
                    EscalationPolicyDefaults.IsEnabled,
                    EscalationPolicyDefaultsDto.Current.Ladder,
                    EscalationPolicyDefaults.OffenseWindowHours,
                    EscalationPolicyDefaults.CountAutoModViolations,
                    EscalationPolicyDefaults.DefaultTimeoutSeconds,
                    EscalationPolicyDefaultsDto.Current,
                    EscalationPolicyLimitsDto.Current
                )
                : ToDto(policy)
        );
    }

    public async Task<Result<ModerationEscalationPolicyDto>> UpsertPolicyAsync(
        Guid broadcasterId,
        UpsertEscalationPolicyRequest request,
        CancellationToken ct = default
    )
    {
        Result<ModerationEscalationPolicyDto>? refused = Validate(request);
        if (refused is not null)
            return refused;

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
        policy.DefaultTimeoutSeconds = request.DefaultTimeoutSeconds;
        await db.SaveChangesAsync(ct);
        return Result.Success(ToDto(policy));
    }

    private static Result<ModerationEscalationPolicyDto> Refuse(
        string message,
        string key,
        string field
    ) =>
        Result.Failure<ModerationEscalationPolicyDto>(
            message,
            "VALIDATION_FAILED",
            errorData: new EscalationPolicyViolation(key, field)
        );

    private static Result<ModerationEscalationPolicyDto>? Validate(
        UpsertEscalationPolicyRequest request
    )
    {
        EscalationPolicyLimitsDto limits = EscalationPolicyLimitsDto.Current;
        if (
            request.OffenseWindowHours < limits.MinOffenseWindowHours
            || request.OffenseWindowHours > limits.MaxOffenseWindowHours
        )
            return Refuse(
                $"The offense window must be {limits.MinOffenseWindowHours} to {limits.MaxOffenseWindowHours} hours.",
                EscalationPolicyViolation.WindowOutOfRange,
                "offenseWindowHours"
            );
        if (
            request.DefaultTimeoutSeconds < limits.MinTimeoutSeconds
            || request.DefaultTimeoutSeconds > limits.MaxTimeoutSeconds
        )
            return Refuse(
                $"The default timeout must be {limits.MinTimeoutSeconds} to {limits.MaxTimeoutSeconds} seconds.",
                EscalationPolicyViolation.TimeoutOutOfRange,
                "defaultTimeoutSeconds"
            );
        if (request.Ladder is null || request.Ladder.Count < limits.MinLadderSteps)
            return Refuse(
                "The ladder needs at least one step.",
                EscalationPolicyViolation.LadderEmpty,
                "ladder"
            );
        if (request.Ladder.Count > limits.MaxLadderSteps)
            return Refuse(
                $"The ladder holds at most {limits.MaxLadderSteps} steps.",
                EscalationPolicyViolation.LadderTooLong,
                "ladder"
            );
        int previous = 0;
        for (int i = 0; i < request.Ladder.Count; i++)
        {
            EscalationLadderStep step = request.Ladder[i];
            if (step.AtOffense <= previous)
                return Refuse(
                    "Ladder steps must be strictly ascending by offense number.",
                    EscalationPolicyViolation.StepsMustAscend,
                    $"ladder[{i}].atOffense"
                );
            if (step.Action is not ("warn" or "timeout" or "ban"))
                return Refuse(
                    $"Unknown ladder action '{step.Action}' - warn, timeout, or ban.",
                    EscalationPolicyViolation.UnknownAction,
                    $"ladder[{i}].action"
                );
            if (
                step.Action == "timeout"
                && (
                    step.TimeoutSeconds is null
                    || step.TimeoutSeconds < limits.MinTimeoutSeconds
                    || step.TimeoutSeconds > limits.MaxTimeoutSeconds
                )
            )
                return Refuse(
                    $"A timeout step needs a duration of {limits.MinTimeoutSeconds} to {limits.MaxTimeoutSeconds} seconds.",
                    EscalationPolicyViolation.TimeoutStepOutOfRange,
                    $"ladder[{i}].timeoutSeconds"
                );
            previous = step.AtOffense;
        }
        return null;
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
            policy.CountAutoModViolations,
            policy.DefaultTimeoutSeconds,
            EscalationPolicyDefaultsDto.Current,
            EscalationPolicyLimitsDto.Current
        );
}
