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
using DomainTimer = NomNomzBot.Domain.Commands.Entities.Timer;

namespace NomNomzBot.Infrastructure.Commands;

public class TimerManagementService : ITimerManagementService
{
    private readonly IApplicationDbContext _db;

    public TimerManagementService(IApplicationDbContext db)
    {
        _db = db;
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
                t.LastFiredAt,
                t.Messages.Count,
                t.CreatedAt
            ))
            .ToListAsync(cancellationToken);

        return Result.Success(
            new PagedList<TimerListItem>(items, total, pagination.Page, pagination.PageSize)
        );
    }

    public async Task<Result<TimerDto>> GetAsync(
        string broadcasterId,
        int id,
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

        DomainTimer timer = new()
        {
            BroadcasterId = broadcaster,
            Name = request.Name,
            Messages = request.Messages,
            IntervalMinutes = request.IntervalMinutes,
            MinChatActivity = request.MinChatActivity,
            IsEnabled = request.IsEnabled,
        };

        _db.Timers.Add(timer);
        await _db.SaveChangesAsync(cancellationToken);

        return Result.Success(ToDto(timer));
    }

    public async Task<Result<TimerDto>> UpdateAsync(
        string broadcasterId,
        int id,
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
            timer.Messages = request.Messages;
        if (request.IntervalMinutes.HasValue)
            timer.IntervalMinutes = request.IntervalMinutes.Value;
        if (request.MinChatActivity.HasValue)
            timer.MinChatActivity = request.MinChatActivity.Value;
        if (request.IsEnabled.HasValue)
            timer.IsEnabled = request.IsEnabled.Value;

        await _db.SaveChangesAsync(cancellationToken);

        return Result.Success(ToDto(timer));
    }

    public async Task<Result> DeleteAsync(
        string broadcasterId,
        int id,
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

        _db.Timers.Remove(timer);
        await _db.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }

    public async Task<Result<TimerDto>> ToggleAsync(
        string broadcasterId,
        int id,
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

        return Result.Success(ToDto(timer));
    }

    private static TimerDto ToDto(DomainTimer t) =>
        new(
            t.Id,
            t.Name,
            t.Messages,
            t.IntervalMinutes,
            t.MinChatActivity,
            t.IsEnabled,
            t.LastFiredAt,
            t.NextMessageIndex,
            t.CreatedAt,
            t.UpdatedAt
        );
}
