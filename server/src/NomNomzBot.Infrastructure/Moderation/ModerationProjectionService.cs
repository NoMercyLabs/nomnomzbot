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
using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Moderation.Dtos;
using NomNomzBot.Application.Moderation.Services;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Moderation.Entities;
using NomNomzBot.Domain.Moderation.Events;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Music;

namespace NomNomzBot.Infrastructure.Moderation;

/// <summary>
/// The J.4/J.5 projections (moderation.md §3.8). The trust score REUSES the shared
/// <see cref="TrustScoreCalculator"/> (never a fork) — the moderation rollup feeds its
/// <c>TimeoutCount</c>/<c>BanCount</c> penalties. Heat is the complementary 0–100 recent-violation
/// pressure with exponential decay (half-life 24 h):
/// <c>heat = clamp(heat × 0.5^(Δt/24h) + delta, 0, 100)</c> — no negative accrual, and the threshold
/// event fires only on an UPWARD crossing of the channel's configured (automod) threshold.
/// </summary>
public sealed class ModerationProjectionService(
    IApplicationDbContext db,
    IModerationService moderation,
    IEventBus eventBus,
    TimeProvider clock,
    ILogger<ModerationProjectionService> logger
) : IModerationProjectionService
{
    private const double HeatHalfLifeHours = 24.0;
    private const int DefaultHeatThreshold = 80;

    /// <summary>The §3.8 per-violation heat deltas (no negative accrual).</summary>
    private static decimal HeatDeltaFor(string actionType) =>
        actionType switch
        {
            "ban" => 40m,
            "timeout" => 15m,
            "report_validated" => 10m,
            "automod_denied" => 5m,
            "filter_hit" => 5m,
            _ => 0m, // warn / unban / delete_message shape the rollup, not the heat
        };

    public async Task<Result> ApplyActionAsync(
        Guid broadcasterId,
        string subjectTwitchUserId,
        string actionType,
        DateTime occurredAtUtc,
        CancellationToken ct = default
    )
    {
        if (string.IsNullOrEmpty(subjectTwitchUserId))
            return Result.Success(); // anonymous/unattributed action — nothing to project

        Guid? subjectUserId = await ResolveUserIdAsync(subjectTwitchUserId, ct);
        if (subjectUserId is null)
        {
            logger.LogDebug(
                "Moderation projection: no local user for Twitch id {TwitchUserId}; skipping",
                subjectTwitchUserId
            );
            return Result.Success();
        }

        UserModerationHistory history = await UpsertHistoryAsync(
            broadcasterId,
            subjectUserId.Value,
            subjectTwitchUserId,
            actionType,
            occurredAtUtc,
            ct
        );
        await RecomputeAsync(
            broadcasterId,
            subjectUserId.Value,
            subjectTwitchUserId,
            history,
            HeatDeltaFor(actionType),
            occurredAtUtc,
            ct
        );
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result> RecomputeTrustAsync(
        Guid broadcasterId,
        Guid subjectUserId,
        CancellationToken ct = default
    )
    {
        UserModerationHistory? history = await db.UserModerationHistories.FirstOrDefaultAsync(
            h => h.BroadcasterId == broadcasterId && h.SubjectUserId == subjectUserId,
            ct
        );
        if (history is null)
            return Result.Failure("No moderation history for this user.", "NOT_FOUND");

        await RecomputeAsync(
            broadcasterId,
            subjectUserId,
            history.SubjectTwitchUserId,
            history,
            heatDelta: 0m,
            clock.GetUtcNow().UtcDateTime,
            ct
        );
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result> RebuildAsync(Guid broadcasterId, CancellationToken ct = default)
    {
        // Wipe and refill from the recorded actions — the projections are rebuildable by contract.
        List<UserModerationHistory> oldHistories = await db
            .UserModerationHistories.Where(h => h.BroadcasterId == broadcasterId)
            .ToListAsync(ct);
        List<UserTrustScore> oldScores = await db
            .UserTrustScores.Where(s => s.BroadcasterId == broadcasterId)
            .ToListAsync(ct);
        foreach (UserModerationHistory h in oldHistories)
            db.UserModerationHistories.Remove(h);
        foreach (UserTrustScore s in oldScores)
            db.UserTrustScores.Remove(s);
        // Flush the wipe BEFORE refilling — otherwise the upserts find the deleted-state tracked rows,
        // mutate them, and the final save deletes the "rebuilt" data with them.
        await db.SaveChangesAsync(ct);

        List<Domain.Platform.Entities.Record> records = await db
            .Records.Where(r =>
                r.BroadcasterId == broadcasterId && r.RecordType == "moderation_action"
            )
            .OrderBy(r => r.CreatedAt)
            .ToListAsync(ct);

        // Refill against a LOCAL identity map — mid-batch store queries cannot see unsaved adds.
        Dictionary<Guid, UserModerationHistory> rebuiltBySubject = new();
        foreach (Domain.Platform.Entities.Record record in records)
        {
            RecordedAction? action;
            try
            {
                action = JsonSerializer.Deserialize<RecordedAction>(record.Data);
            }
            catch (JsonException)
            {
                continue; // a non-action or malformed row never poisons the rebuild
            }
            if (action is null || string.IsNullOrEmpty(action.TargetUserId))
                continue;

            Guid? subjectUserId = await ResolveUserIdAsync(action.TargetUserId, ct);
            if (subjectUserId is null)
                continue;

            if (
                !rebuiltBySubject.TryGetValue(
                    subjectUserId.Value,
                    out UserModerationHistory? history
                )
            )
            {
                history = new UserModerationHistory
                {
                    BroadcasterId = broadcasterId,
                    SubjectUserId = subjectUserId.Value,
                    SubjectTwitchUserId = action.TargetUserId,
                    FirstSeenAt = record.CreatedAt,
                };
                db.UserModerationHistories.Add(history);
                rebuiltBySubject[subjectUserId.Value] = history;
            }
            ApplyCounts(history, action.Action ?? "", record.CreatedAt);
        }
        await db.SaveChangesAsync(ct);

        // Heat is decayed, transient state — a rebuild restarts it at zero; trust re-derives from the rollup.
        List<UserModerationHistory> rebuilt = await db
            .UserModerationHistories.Where(h => h.BroadcasterId == broadcasterId)
            .ToListAsync(ct);
        foreach (UserModerationHistory history in rebuilt)
            await RecomputeAsync(
                broadcasterId,
                history.SubjectUserId,
                history.SubjectTwitchUserId,
                history,
                heatDelta: 0m,
                clock.GetUtcNow().UtcDateTime,
                ct
            );
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    // ─── Internals ───────────────────────────────────────────────────────────

    private async Task<Guid?> ResolveUserIdAsync(string twitchUserId, CancellationToken ct)
    {
        Guid id = await db
            .Users.Where(u => u.TwitchUserId == twitchUserId)
            .Select(u => u.Id)
            .FirstOrDefaultAsync(ct);
        return id == Guid.Empty ? null : id;
    }

    private async Task<UserModerationHistory> UpsertHistoryAsync(
        Guid broadcasterId,
        Guid subjectUserId,
        string subjectTwitchUserId,
        string actionType,
        DateTime occurredAtUtc,
        CancellationToken ct
    )
    {
        UserModerationHistory? history = await db.UserModerationHistories.FirstOrDefaultAsync(
            h => h.BroadcasterId == broadcasterId && h.SubjectUserId == subjectUserId,
            ct
        );
        if (history is null)
        {
            history = new UserModerationHistory
            {
                BroadcasterId = broadcasterId,
                SubjectUserId = subjectUserId,
                SubjectTwitchUserId = subjectTwitchUserId,
                FirstSeenAt = occurredAtUtc,
            };
            db.UserModerationHistories.Add(history);
        }

        ApplyCounts(history, actionType, occurredAtUtc);
        return history;
    }

    /// <summary>Bumps the J.4 rollup for one action — shared by the incremental path and the rebuild.</summary>
    private static void ApplyCounts(
        UserModerationHistory history,
        string actionType,
        DateTime occurredAtUtc
    )
    {
        switch (actionType)
        {
            case "ban":
                history.BanCount++;
                break;
            case "timeout":
                history.TimeoutCount++;
                break;
            case "warn":
                history.WarningCount++;
                break;
            case "delete_message":
                history.MessagesDeletedCount++;
                break;
        }
        history.LastActionAt = occurredAtUtc;
        history.LastActionType = actionType;
    }

    private async Task RecomputeAsync(
        Guid broadcasterId,
        Guid subjectUserId,
        string subjectTwitchUserId,
        UserModerationHistory history,
        decimal heatDelta,
        DateTime nowUtc,
        CancellationToken ct
    )
    {
        UserTrustScore? score = await db.UserTrustScores.FirstOrDefaultAsync(
            s => s.BroadcasterId == broadcasterId && s.SubjectUserId == subjectUserId,
            ct
        );
        if (score is null)
        {
            score = new UserTrustScore
            {
                BroadcasterId = broadcasterId,
                SubjectUserId = subjectUserId,
                SubjectTwitchUserId = subjectTwitchUserId,
            };
            db.UserTrustScores.Add(score);
        }

        // Trust: the SHARED calculator (never forked). The positive base signal is the subject's local
        // tenure (how long we have known them — User.CreatedAt); the rollup's TimeoutCount/BanCount are
        // the calculator's own violation penalties, so a punished viewer always scores below a clean one
        // of equal tenure. Richer community signals (follow age, sub/VIP) enrich this the same way later.
        DateTime firstKnown = await db
            .Users.Where(u => u.Id == subjectUserId)
            .Select(u => u.CreatedAt)
            .FirstOrDefaultAsync(ct);
        double tenureMonths =
            firstKnown == default ? 0.0 : Math.Max(0.0, (nowUtc - firstKnown).TotalDays / 30.44);
        double trust = TrustScoreCalculator.Calculate(
            new TrustContext
            {
                AccountAgeMonths = tenureMonths,
                TimeoutCount = history.TimeoutCount,
                BanCount = history.BanCount,
            }
        );
        score.TrustScore = Math.Clamp((decimal)trust, 0m, 100m);

        // Heat: exponential decay since the last heat event, then the delta, clamped to [0, 100].
        decimal decayed =
            score.LastHeatEventAt is DateTime last && score.HeatScore > 0m
                ? score.HeatScore
                    * (decimal)Math.Pow(0.5, (nowUtc - last).TotalHours / HeatHalfLifeHours)
                : 0m;
        decimal before = decayed;
        decimal after = Math.Clamp(decayed + heatDelta, 0m, 100m);
        score.HeatScore = after;
        if (heatDelta > 0m)
            score.LastHeatEventAt = nowUtc;
        score.ComputedAt = nowUtc;

        // The threshold event fires ONLY on the upward crossing.
        int threshold = await HeatThresholdAsync(broadcasterId, ct);
        if (before < threshold && after >= threshold)
            await eventBus.PublishAsync(
                new UserHeatThresholdCrossedEvent
                {
                    BroadcasterId = broadcasterId,
                    SubjectUserId = subjectUserId,
                    SubjectTwitchUserId = subjectTwitchUserId,
                    HeatScore = after,
                    Threshold = threshold,
                },
                ct
            );
    }

    /// <summary>The channel's configured heat threshold; stored configs predating the field read as the default.</summary>
    private async Task<int> HeatThresholdAsync(Guid broadcasterId, CancellationToken ct)
    {
        Result<AutomodConfigDto> config = await moderation.GetAutomodConfigAsync(
            broadcasterId.ToString(),
            ct
        );
        return config is { IsSuccess: true, Value.HeatTimeoutThreshold: > 0 }
            ? config.Value.HeatTimeoutThreshold
            : DefaultHeatThreshold;
    }

    /// <summary>The recorded action shape (the fields every writer of <c>moderation_action</c> rows shares).</summary>
    private sealed class RecordedAction
    {
        public string? Action { get; set; }
        public string? TargetUserId { get; set; }
    }
}
