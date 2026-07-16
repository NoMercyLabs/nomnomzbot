// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Rewards.Dtos;
using NomNomzBot.Application.Rewards.Services;
using NomNomzBot.Domain.Rewards.Entities;

namespace NomNomzBot.Infrastructure.Rewards;

/// <summary>
/// <see cref="IRedemptionTimerService"/> over the <see cref="RedemptionTimer"/> rows. The countdown is
/// clock-derived (see the entity doc): pause folds the elapsed run time into <c>RemainingSeconds</c>,
/// resume re-anchors <c>RunningSince</c>, and rows are written only on state changes. Completing —
/// manually or via <see cref="CompleteDueAsync"/> — fulfills the redemption on Twitch through
/// <see cref="IRewardService.SetRedemptionStatusAsync"/>; a fulfill failure (unmanageable reward, dead
/// token) degrades gracefully: the timer still completes and the failure is logged, never thrown.
/// </summary>
public sealed class RedemptionTimerService : IRedemptionTimerService
{
    private const int TerminalHistoryTake = 20;

    private readonly IApplicationDbContext _db;
    private readonly IRewardService _rewards;
    private readonly TimeProvider _clock;
    private readonly ILogger<RedemptionTimerService> _logger;

    public RedemptionTimerService(
        IApplicationDbContext db,
        IRewardService rewards,
        TimeProvider clock,
        ILogger<RedemptionTimerService> logger
    )
    {
        _db = db;
        _rewards = rewards;
        _clock = clock;
        _logger = logger;
    }

    public async Task<Result<RedemptionTimerDto>> StartAsync(
        Guid broadcasterId,
        string redemptionId,
        string rewardId,
        string rewardTitle,
        string redeemedByDisplayName,
        int durationSeconds,
        CancellationToken cancellationToken = default
    )
    {
        if (durationSeconds <= 0)
            return Result.Failure<RedemptionTimerDto>(
                "A countdown needs a positive duration.",
                "VALIDATION_FAILED"
            );

        // Idempotent per redemption: Twitch redelivers webhooks/EventSub — the first timer wins.
        RedemptionTimer? existing = await _db.RedemptionTimers.FirstOrDefaultAsync(
            t => t.BroadcasterId == broadcasterId && t.RedemptionId == redemptionId,
            cancellationToken
        );
        if (existing is not null)
            return Result.Success(ToDto(existing, NowUtc()));

        DateTime now = NowUtc();
        RedemptionTimer timer = new()
        {
            Id = Guid.CreateVersion7(),
            BroadcasterId = broadcasterId,
            RedemptionId = redemptionId,
            RewardId = rewardId,
            RewardTitle = rewardTitle,
            RedeemedByDisplayName = redeemedByDisplayName,
            DurationSeconds = durationSeconds,
            RemainingSeconds = durationSeconds,
            RunningSince = now,
            Status = RedemptionTimerStatus.Running,
            StartedAt = now,
            UpdatedAt = now,
        };
        _db.RedemptionTimers.Add(timer);
        await _db.SaveChangesAsync(cancellationToken);

        return Result.Success(ToDto(timer, now));
    }

    public async Task<Result<IReadOnlyList<RedemptionTimerDto>>> ListAsync(
        string broadcasterId,
        CancellationToken cancellationToken = default
    )
    {
        if (!Guid.TryParse(broadcasterId, out Guid broadcaster))
            return Result.Failure<IReadOnlyList<RedemptionTimerDto>>(
                $"Invalid channel ID '{broadcasterId}'.",
                "VALIDATION_FAILED"
            );

        List<RedemptionTimer> active = await _db
            .RedemptionTimers.Where(t =>
                t.BroadcasterId == broadcaster
                && (
                    t.Status == RedemptionTimerStatus.Running
                    || t.Status == RedemptionTimerStatus.Paused
                )
            )
            .OrderBy(t => t.StartedAt)
            .ToListAsync(cancellationToken);
        List<RedemptionTimer> recent = await _db
            .RedemptionTimers.Where(t =>
                t.BroadcasterId == broadcaster
                && t.Status != RedemptionTimerStatus.Running
                && t.Status != RedemptionTimerStatus.Paused
            )
            .OrderByDescending(t => t.UpdatedAt)
            .Take(TerminalHistoryTake)
            .ToListAsync(cancellationToken);

        DateTime now = NowUtc();
        IReadOnlyList<RedemptionTimerDto> items =
        [
            .. active.Select(t => ToDto(t, now)),
            .. recent.Select(t => ToDto(t, now)),
        ];
        return Result.Success(items);
    }

    public Task<Result<RedemptionTimerDto>> PauseAsync(
        string broadcasterId,
        Guid timerId,
        CancellationToken cancellationToken = default
    ) =>
        MutateAsync(
            broadcasterId,
            timerId,
            (timer, now) =>
            {
                if (timer.Status != RedemptionTimerStatus.Running)
                    return Result.Failure<RedemptionTimer>(
                        "Only a running timer can be paused.",
                        "CONFLICT"
                    );
                timer.RemainingSeconds = LiveRemaining(timer, now);
                timer.RunningSince = null;
                timer.Status = RedemptionTimerStatus.Paused;
                return Result.Success(timer);
            },
            cancellationToken
        );

    public Task<Result<RedemptionTimerDto>> ResumeAsync(
        string broadcasterId,
        Guid timerId,
        CancellationToken cancellationToken = default
    ) =>
        MutateAsync(
            broadcasterId,
            timerId,
            (timer, now) =>
            {
                if (timer.Status != RedemptionTimerStatus.Paused)
                    return Result.Failure<RedemptionTimer>(
                        "Only a paused timer can be resumed.",
                        "CONFLICT"
                    );
                timer.RunningSince = now;
                timer.Status = RedemptionTimerStatus.Running;
                return Result.Success(timer);
            },
            cancellationToken
        );

    public async Task<Result<RedemptionTimerDto>> CompleteAsync(
        string broadcasterId,
        Guid timerId,
        CancellationToken cancellationToken = default
    )
    {
        Result<RedemptionTimerDto> completed = await MutateAsync(
            broadcasterId,
            timerId,
            (timer, now) =>
            {
                if (
                    timer.Status != RedemptionTimerStatus.Running
                    && timer.Status != RedemptionTimerStatus.Paused
                )
                    return Result.Failure<RedemptionTimer>(
                        "The timer has already finished.",
                        "CONFLICT"
                    );
                timer.RemainingSeconds = 0;
                timer.RunningSince = null;
                timer.Status = RedemptionTimerStatus.Completed;
                return Result.Success(timer);
            },
            cancellationToken
        );
        if (completed.IsFailure)
            return completed;

        await FulfillAsync(broadcasterId, completed.Value.RedemptionId, cancellationToken);
        return completed;
    }

    public Task<Result<RedemptionTimerDto>> CancelAsync(
        string broadcasterId,
        Guid timerId,
        CancellationToken cancellationToken = default
    ) =>
        MutateAsync(
            broadcasterId,
            timerId,
            (timer, now) =>
            {
                if (
                    timer.Status != RedemptionTimerStatus.Running
                    && timer.Status != RedemptionTimerStatus.Paused
                )
                    return Result.Failure<RedemptionTimer>(
                        "The timer has already finished.",
                        "CONFLICT"
                    );
                timer.RemainingSeconds = LiveRemaining(timer, now);
                timer.RunningSince = null;
                timer.Status = RedemptionTimerStatus.Canceled;
                return Result.Success(timer);
            },
            cancellationToken
        );

    public async Task<int> CompleteDueAsync(CancellationToken cancellationToken = default)
    {
        DateTime now = NowUtc();
        // The index narrows to running rows; expiry itself is clock math on the loaded few.
        List<RedemptionTimer> running = await _db
            .RedemptionTimers.Where(t => t.Status == RedemptionTimerStatus.Running)
            .ToListAsync(cancellationToken);

        List<RedemptionTimer> due = running.Where(t => LiveRemaining(t, now) <= 0).ToList();
        if (due.Count == 0)
            return 0;

        foreach (RedemptionTimer timer in due)
        {
            timer.RemainingSeconds = 0;
            timer.RunningSince = null;
            timer.Status = RedemptionTimerStatus.Completed;
            timer.UpdatedAt = now;
        }
        await _db.SaveChangesAsync(cancellationToken);

        foreach (RedemptionTimer timer in due)
            await FulfillAsync(
                timer.BroadcasterId.ToString(),
                timer.RedemptionId,
                cancellationToken
            );

        return due.Count;
    }

    /// <summary>Best-effort Twitch fulfill: an unmanageable reward or a dead token must not undo the timer.</summary>
    private async Task FulfillAsync(string broadcasterId, string redemptionId, CancellationToken ct)
    {
        Result fulfilled = await _rewards.SetRedemptionStatusAsync(
            broadcasterId,
            redemptionId,
            "FULFILLED",
            ct
        );
        if (fulfilled.IsFailure)
            _logger.LogWarning(
                "Redemption timer completed but the Twitch fulfill failed for {RedemptionId} in {Channel}: {Error} ({Code})",
                redemptionId,
                broadcasterId,
                fulfilled.ErrorMessage,
                fulfilled.ErrorCode
            );
    }

    private async Task<Result<RedemptionTimerDto>> MutateAsync(
        string broadcasterId,
        Guid timerId,
        Func<RedemptionTimer, DateTime, Result<RedemptionTimer>> transition,
        CancellationToken ct
    )
    {
        if (!Guid.TryParse(broadcasterId, out Guid broadcaster))
            return Result.Failure<RedemptionTimerDto>(
                $"Invalid channel ID '{broadcasterId}'.",
                "VALIDATION_FAILED"
            );

        RedemptionTimer? timer = await _db.RedemptionTimers.FirstOrDefaultAsync(
            t => t.Id == timerId && t.BroadcasterId == broadcaster,
            ct
        );
        if (timer is null)
            return Errors.NotFound<RedemptionTimerDto>("RedemptionTimer", timerId.ToString());

        DateTime now = NowUtc();
        Result<RedemptionTimer> transitioned = transition(timer, now);
        if (transitioned.IsFailure)
            return Result.Failure<RedemptionTimerDto>(
                transitioned.ErrorMessage!,
                transitioned.ErrorCode
            );

        timer.UpdatedAt = now;
        await _db.SaveChangesAsync(ct);
        return Result.Success(ToDto(timer, now));
    }

    private DateTime NowUtc() => _clock.GetUtcNow().UtcDateTime;

    /// <summary>The live countdown value: exact while paused/terminal, clock-derived while running.</summary>
    internal static int LiveRemaining(RedemptionTimer timer, DateTime nowUtc) =>
        timer.Status == RedemptionTimerStatus.Running && timer.RunningSince is DateTime since
            ? Math.Max(0, timer.RemainingSeconds - (int)(nowUtc - since).TotalSeconds)
            : timer.RemainingSeconds;

    private static RedemptionTimerDto ToDto(RedemptionTimer t, DateTime nowUtc) =>
        new(
            t.Id,
            t.RedemptionId,
            t.RewardId,
            t.RewardTitle,
            t.RedeemedByDisplayName,
            t.DurationSeconds,
            LiveRemaining(t, nowUtc),
            t.Status,
            t.StartedAt
        );
}
