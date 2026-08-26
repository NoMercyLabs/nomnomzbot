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
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Application.Commands.Dtos;
using NomNomzBot.Application.Commands.Services;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Billing;
using NomNomzBot.Application.DTOs.Billing;
using NomNomzBot.Domain.Platform.Events;
using NomNomzBot.Domain.Platform.Interfaces;
using DomainTimer = NomNomzBot.Domain.Commands.Entities.Timer;

namespace NomNomzBot.Infrastructure.Commands;

public class TimerManagementService : ITimerManagementService
{
    private readonly IApplicationDbContext _db;
    private readonly IEventBus _eventBus;
    private readonly IResourceQuotaService _quota;
    private readonly ITemplateHelperValidator _templateHelperValidator;

    public TimerManagementService(
        IApplicationDbContext db,
        IEventBus eventBus,
        IResourceQuotaService quota,
        ITemplateHelperValidator templateHelperValidator
    )
    {
        _db = db;
        _eventBus = eventBus;
        _quota = quota;
        _templateHelperValidator = templateHelperValidator;
    }

    /// <summary>S042 save-time guard: a timer message never carries {{args.*}}, {{user.*}}, or
    /// {{target.*}} — no chatter triggers a timer — checked against the Timer-context registry before
    /// any row is written.</summary>
    private Result ValidateTemplateHelpers(IEnumerable<string> messages)
    {
        foreach (string message in messages)
        {
            Result result = _templateHelperValidator.Validate(message, TemplateHelperContext.Timer);
            if (result.IsFailure)
                return result;
        }

        return Result.Success();
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

        // timers is NEAR_FREE (S-BUDGETS-a): checked against the registry's uniform safety baseline via the
        // quota seam — never tier-scaled, self-host included. The count comes from the same
        // GetCurrentCountAsync the read-only usage report (S-BUDGETS-b1) uses, so the two can never disagree.
        Result<long> countResult = await _quota.GetCurrentCountAsync(
            broadcaster,
            "timers",
            cancellationToken
        );
        if (countResult.IsFailure)
            return countResult.ToTyped<TimerDto>();
        long existingTimerCount = countResult.Value;
        Result<QuotaCheckDto> timerQuota = await _quota.CheckAsync(
            broadcaster,
            "timers",
            existingTimerCount + 1,
            cancellationToken
        );
        if (timerQuota.IsFailure)
            return timerQuota.ToTyped<TimerDto>();
        if (!timerQuota.Value.Allowed)
            return Errors.QuotaExceeded("timers", timerQuota.Value.Limit).ToTyped<TimerDto>();

        Result variationsOk = await CheckVariationCapAsync(
            broadcaster,
            request.Messages.Count,
            cancellationToken
        );
        if (variationsOk.IsFailure)
            return variationsOk.ToTyped<TimerDto>();

        Result helperOk = ValidateTemplateHelpers(request.Messages);
        if (helperOk.IsFailure)
            return helperOk.ToTyped<TimerDto>();

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

            Result helperOk = ValidateTemplateHelpers(request.Messages);
            if (helperOk.IsFailure)
                return helperOk.ToTyped<TimerDto>();

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

    /// <summary>
    /// The per-timer variation cap (<c>response_variations_per_trigger</c>) — NEAR_FREE, the registry's
    /// uniform safety baseline, never tier-scaled.
    /// </summary>
    private async Task<Result> CheckVariationCapAsync(
        Guid broadcaster,
        int requestedCount,
        CancellationToken ct
    )
    {
        Result<QuotaCheckDto> check = await _quota.CheckAsync(
            broadcaster,
            "response_variations_per_trigger",
            requestedCount,
            ct
        );
        if (check.IsFailure)
            return check;
        return check.Value.Allowed
            ? Result.Success()
            : Errors.QuotaExceeded("message variations per timer", check.Value.Limit);
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
