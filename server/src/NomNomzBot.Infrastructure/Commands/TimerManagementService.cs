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
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Commands.Dtos;
using NomNomzBot.Application.Commands.Services;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Billing;
using NomNomzBot.Domain.Platform.Events;
using NomNomzBot.Domain.Platform.Interfaces;
using DomainTimer = NomNomzBot.Domain.Commands.Entities.Timer;

namespace NomNomzBot.Infrastructure.Commands;

public class TimerManagementService : ITimerManagementService
{
    private readonly IApplicationDbContext _db;
    private readonly IEventBus _eventBus;
    private readonly IBillingTierService _tiers;

    public TimerManagementService(
        IApplicationDbContext db,
        IEventBus eventBus,
        IBillingTierService tiers
    )
    {
        _db = db;
        _eventBus = eventBus;
        _tiers = tiers;
    }

    public async Task<Result<PagedList<TimerListItem>>> ListAsync(
        string broadcasterId,
        PaginationParams pagination,
        CancellationToken cancellationToken = default
    )
    {
        if (!Guid.TryParse(broadcasterId, out Guid broadcaster))
            return Result.Failure<PagedList<TimerListItem>>(
                $"Invalid channel ID '{broadcasterId}'.",
                "VALIDATION_FAILED"
            );

        IQueryable<DomainTimer> query = _db.Timers.Where(t => t.BroadcasterId == broadcaster);
        int total = await query.CountAsync(cancellationToken);

        List<TimerListItem> items = await query
            .OrderBy(t => t.Name)
            .Skip((pagination.Page - 1) * pagination.PageSize)
            .Take(pagination.PageSize)
            .Select(t => new TimerListItem(
                t.Id,
                t.Name,
                t.IntervalMinutes,
                t.IsEnabled,
                t.FireOnce,
                t.LastFiredAt,
                t.Messages.Count,
                t.CreatedAt
            ))
            .ToListAsync(cancellationToken);

        return Result.Success(
            new PagedList<TimerListItem>(items, pagination.Page, pagination.PageSize, total)
        );
    }

    public async Task<Result<TimerDto>> GetAsync(
        string broadcasterId,
        Guid id,
        CancellationToken cancellationToken = default
    )
    {
        if (!Guid.TryParse(broadcasterId, out Guid broadcaster))
            return Result.Failure<TimerDto>(
                $"Invalid channel ID '{broadcasterId}'.",
                "VALIDATION_FAILED"
            );

        DomainTimer? timer = await _db.Timers.FirstOrDefaultAsync(
            t => t.BroadcasterId == broadcaster && t.Id == id,
            cancellationToken
        );

        if (timer is null)
            return Errors.NotFound<TimerDto>("Timer", id.ToString());

        return Result.Success(ToDto(timer));
    }

    public async Task<Result<TimerDto>> CreateAsync(
        string broadcasterId,
        CreateTimerDto request,
        CancellationToken cancellationToken = default
    )
    {
        if (!Guid.TryParse(broadcasterId, out Guid broadcaster))
            return Result.Failure<TimerDto>(
                $"Invalid channel ID '{broadcasterId}'.",
                "VALIDATION_FAILED"
            );

        bool exists = await _db.Timers.AnyAsync(
            t => t.BroadcasterId == broadcaster && t.Name == request.Name,
            cancellationToken
        );

        if (exists)
            return Errors.AlreadyExists("timer", request.Name).ToTyped<TimerDto>();

        // Tier quotas (monetization-billing §3.3): the timer count and the per-timer message-variation
        // list are both capped by the plan; -1 (self-host / unseeded) is unlimited.
        Result<long> timerCap = await _tiers.GetLimitAsync(
            broadcaster,
            "timers",
            cancellationToken
        );
        if (timerCap is { IsSuccess: true, Value: >= 0 })
        {
            int current = await _db.Timers.CountAsync(
                t => t.BroadcasterId == broadcaster,
                cancellationToken
            );
            if (current >= timerCap.Value)
                return Errors.QuotaExceeded("timers", timerCap.Value).ToTyped<TimerDto>();
        }

        Result variationsOk = await CheckVariationCapAsync(
            broadcaster,
            request.Messages.Count,
            cancellationToken
        );
        if (variationsOk.IsFailure)
            return variationsOk.ToTyped<TimerDto>();

        DomainTimer timer = new()
        {
            BroadcasterId = broadcaster,
            Name = request.Name,
            Messages = request.Messages,
            PipelineId = request.PipelineId,
            IntervalMinutes = request.IntervalMinutes,
            MinChatActivity = request.MinChatActivity,
            IsEnabled = request.IsEnabled,
            FireOnce = request.FireOnce,
        };

        _db.Timers.Add(timer);
        await _db.SaveChangesAsync(cancellationToken);
        await PublishConfigChangedAsync(broadcaster, timer.Id, "created", cancellationToken);

        return Result.Success(ToDto(timer));
    }

    public async Task<Result<TimerDto>> UpdateAsync(
        string broadcasterId,
        Guid id,
        UpdateTimerDto request,
        CancellationToken cancellationToken = default
    )
    {
        if (!Guid.TryParse(broadcasterId, out Guid broadcaster))
            return Result.Failure<TimerDto>(
                $"Invalid channel ID '{broadcasterId}'.",
                "VALIDATION_FAILED"
            );

        DomainTimer? timer = await _db.Timers.FirstOrDefaultAsync(
            t => t.BroadcasterId == broadcaster && t.Id == id,
            cancellationToken
        );

        if (timer is null)
            return Errors.NotFound<TimerDto>("Timer", id.ToString());

        if (request.Name is not null)
            timer.Name = request.Name;
        if (request.Messages is not null)
        {
            Result variationsOk = await CheckVariationCapAsync(
                broadcaster,
                request.Messages.Count,
                cancellationToken
            );
            if (variationsOk.IsFailure)
                return variationsOk.ToTyped<TimerDto>();
            timer.Messages = request.Messages;
        }
        // Absent leaves the binding unchanged; Guid.Empty clears it; a real id binds that pipeline (the same
        // sentinel convention RewardService uses — a null pipelineId is dropped by the client's explicitNulls=false
        // serializer, so "clear" cannot ride a null and needs the empty sentinel instead).
        if (request.PipelineId.HasValue)
            timer.PipelineId =
                request.PipelineId.Value == Guid.Empty ? null : request.PipelineId.Value;
        if (request.IntervalMinutes.HasValue)
            timer.IntervalMinutes = request.IntervalMinutes.Value;
        if (request.MinChatActivity.HasValue)
            timer.MinChatActivity = request.MinChatActivity.Value;
        if (request.IsEnabled.HasValue)
            timer.IsEnabled = request.IsEnabled.Value;
        if (request.FireOnce.HasValue)
            timer.FireOnce = request.FireOnce.Value;

        await _db.SaveChangesAsync(cancellationToken);
        await PublishConfigChangedAsync(broadcaster, timer.Id, "updated", cancellationToken);

        return Result.Success(ToDto(timer));
    }

    public async Task<Result> DeleteAsync(
        string broadcasterId,
        Guid id,
        CancellationToken cancellationToken = default
    )
    {
        if (!Guid.TryParse(broadcasterId, out Guid broadcaster))
            return Result.Failure($"Invalid channel ID '{broadcasterId}'.", "VALIDATION_FAILED");

        DomainTimer? timer = await _db.Timers.FirstOrDefaultAsync(
            t => t.BroadcasterId == broadcaster && t.Id == id,
            cancellationToken
        );

        if (timer is null)
            return Result.Failure($"Timer '{id}' was not found.", "NOT_FOUND");

        Guid timerId = timer.Id;
        _db.Timers.Remove(timer);
        await _db.SaveChangesAsync(cancellationToken);
        await PublishConfigChangedAsync(broadcaster, timerId, "deleted", cancellationToken);

        return Result.Success();
    }

    public async Task<Result<TimerDto>> ToggleAsync(
        string broadcasterId,
        Guid id,
        CancellationToken cancellationToken = default
    )
    {
        if (!Guid.TryParse(broadcasterId, out Guid broadcaster))
            return Result.Failure<TimerDto>(
                $"Invalid channel ID '{broadcasterId}'.",
                "VALIDATION_FAILED"
            );

        DomainTimer? timer = await _db.Timers.FirstOrDefaultAsync(
            t => t.BroadcasterId == broadcaster && t.Id == id,
            cancellationToken
        );

        if (timer is null)
            return Errors.NotFound<TimerDto>("Timer", id.ToString());

        timer.IsEnabled = !timer.IsEnabled;
        await _db.SaveChangesAsync(cancellationToken);
        await PublishConfigChangedAsync(broadcaster, timer.Id, "toggled", cancellationToken);

        return Result.Success(ToDto(timer));
    }

    /// <summary>The per-trigger variation cap (<c>response_variations_per_trigger</c>) — -1 is unlimited.</summary>
    private async Task<Result> CheckVariationCapAsync(
        Guid broadcaster,
        int requestedCount,
        CancellationToken ct
    )
    {
        Result<long> cap = await _tiers.GetLimitAsync(
            broadcaster,
            "response_variations_per_trigger",
            ct
        );
        return cap is { IsSuccess: true, Value: >= 0 } && requestedCount > cap.Value
            ? Errors.QuotaExceeded("message variations per timer", cap.Value)
            : Result.Success();
    }

    /// <summary>E5 dashboard live-sync: fired after every successful write so other open dashboards refetch.</summary>
    private Task PublishConfigChangedAsync(
        Guid broadcasterId,
        Guid timerId,
        string action,
        CancellationToken cancellationToken
    ) =>
        _eventBus.PublishAsync(
            new ChannelConfigChangedEvent
            {
                BroadcasterId = broadcasterId,
                Domain = "timers",
                EntityId = timerId.ToString(),
                Action = action,
            },
            cancellationToken
        );

    private static TimerDto ToDto(DomainTimer t) =>
        new(
            t.Id,
            t.Name,
            t.Messages,
            t.IntervalMinutes,
            t.MinChatActivity,
            t.IsEnabled,
            t.FireOnce,
            t.PipelineId,
            t.LastFiredAt,
            t.NextMessageIndex,
            t.CreatedAt,
            t.UpdatedAt
        );
}
