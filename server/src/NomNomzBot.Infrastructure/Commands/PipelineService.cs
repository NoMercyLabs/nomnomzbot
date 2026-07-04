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
using NomNomzBot.Application.Commands.Dtos;
using NomNomzBot.Application.Commands.Services;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Domain.Platform.Events;
using NomNomzBot.Domain.Platform.Interfaces;
using PipelineEntity = NomNomzBot.Domain.Commands.Entities.Pipeline;

namespace NomNomzBot.Infrastructure.Commands;

public class PipelineService : IPipelineService
{
    private readonly IApplicationDbContext _db;
    private readonly IEventBus _eventBus;

    public PipelineService(IApplicationDbContext db, IEventBus eventBus)
    {
        _db = db;
        _eventBus = eventBus;
    }

    public async Task<Result<PagedList<PipelineListItemDto>>> ListAsync(
        string broadcasterId,
        PaginationParams pagination,
        CancellationToken ct = default
    )
    {
        if (!Guid.TryParse(broadcasterId, out Guid broadcaster))
            return Result.Failure<PagedList<PipelineListItemDto>>(
                $"Invalid channel ID '{broadcasterId}'.",
                "VALIDATION_FAILED"
            );

        IQueryable<PipelineEntity> query = _db.Pipelines.Where(p => p.BroadcasterId == broadcaster);
        int total = await query.CountAsync(ct);

        List<PipelineListItemDto> items = await query
            .OrderBy(p => p.Name)
            .Skip((pagination.Page - 1) * pagination.PageSize)
            .Take(pagination.PageSize)
            .Select(p => new PipelineListItemDto(
                p.Id,
                p.Name,
                p.Description,
                p.IsEnabled,
                p.TriggerCount,
                p.LastTriggeredAt,
                p.UpdatedAt
            ))
            .ToListAsync(ct);

        return Result.Success(
            new PagedList<PipelineListItemDto>(items, pagination.Page, pagination.PageSize, total)
        );
    }

    public async Task<Result<PipelineDto>> GetAsync(
        string broadcasterId,
        Guid id,
        CancellationToken ct = default
    )
    {
        if (!Guid.TryParse(broadcasterId, out Guid broadcaster))
            return Result.Failure<PipelineDto>(
                $"Invalid channel ID '{broadcasterId}'.",
                "VALIDATION_FAILED"
            );

        PipelineEntity? entity = await _db.Pipelines.FirstOrDefaultAsync(
            p => p.BroadcasterId == broadcaster && p.Id == id,
            ct
        );

        if (entity is null)
            return Errors.NotFound<PipelineDto>("Pipeline", id.ToString());

        return Result.Success(ToDto(entity));
    }

    public async Task<Result<PipelineDto>> CreateAsync(
        string broadcasterId,
        CreatePipelineDto request,
        CancellationToken ct = default
    )
    {
        if (!Guid.TryParse(broadcasterId, out Guid broadcaster))
            return Result.Failure<PipelineDto>(
                $"Invalid channel ID '{broadcasterId}'.",
                "VALIDATION_FAILED"
            );

        PipelineEntity entity = new()
        {
            BroadcasterId = broadcaster,
            Name = request.Name,
            Description = request.Description,
            IsEnabled = request.IsEnabled,
            TriggerKind = request.TriggerKind,
            GraphJsonCache = request.GraphJsonCache is not null
                ? JsonSerializer.Serialize(request.GraphJsonCache)
                : null,
        };

        _db.Pipelines.Add(entity);
        await _db.SaveChangesAsync(ct);
        await PublishConfigChangedAsync(broadcaster, entity.Id, "created", ct);

        return Result.Success(ToDto(entity));
    }

    public async Task<Result<PipelineDto>> UpdateAsync(
        string broadcasterId,
        Guid id,
        UpdatePipelineDto request,
        CancellationToken ct = default
    )
    {
        if (!Guid.TryParse(broadcasterId, out Guid broadcaster))
            return Result.Failure<PipelineDto>(
                $"Invalid channel ID '{broadcasterId}'.",
                "VALIDATION_FAILED"
            );

        PipelineEntity? entity = await _db.Pipelines.FirstOrDefaultAsync(
            p => p.BroadcasterId == broadcaster && p.Id == id,
            ct
        );

        if (entity is null)
            return Errors.NotFound<PipelineDto>("Pipeline", id.ToString());

        if (request.Name is not null)
            entity.Name = request.Name;
        if (request.Description is not null)
            entity.Description = request.Description;
        if (request.IsEnabled.HasValue)
            entity.IsEnabled = request.IsEnabled.Value;
        if (request.TriggerKind is not null)
            entity.TriggerKind = request.TriggerKind;
        if (request.GraphJsonCache is not null)
            entity.GraphJsonCache = JsonSerializer.Serialize(request.GraphJsonCache);

        await _db.SaveChangesAsync(ct);
        await PublishConfigChangedAsync(broadcaster, entity.Id, "updated", ct);

        return Result.Success(ToDto(entity));
    }

    public async Task<Result> DeleteAsync(
        string broadcasterId,
        Guid id,
        CancellationToken ct = default
    )
    {
        if (!Guid.TryParse(broadcasterId, out Guid broadcaster))
            return Result.Failure($"Invalid channel ID '{broadcasterId}'.", "VALIDATION_FAILED");

        PipelineEntity? entity = await _db.Pipelines.FirstOrDefaultAsync(
            p => p.BroadcasterId == broadcaster && p.Id == id,
            ct
        );

        if (entity is null)
            return Result.Failure($"Pipeline '{id}' was not found.", "NOT_FOUND");

        Guid pipelineId = entity.Id;
        _db.Pipelines.Remove(entity);
        await _db.SaveChangesAsync(ct);
        await PublishConfigChangedAsync(broadcaster, pipelineId, "deleted", ct);

        return Result.Success();
    }

    /// <summary>E5 dashboard live-sync: fired after every successful write so other open dashboards refetch.</summary>
    private Task PublishConfigChangedAsync(
        Guid broadcasterId,
        Guid pipelineId,
        string action,
        CancellationToken ct
    ) =>
        _eventBus.PublishAsync(
            new ChannelConfigChangedEvent
            {
                BroadcasterId = broadcasterId,
                Domain = "pipelines",
                EntityId = pipelineId.ToString(),
                Action = action,
            },
            ct
        );

    private static PipelineDto ToDto(PipelineEntity p)
    {
        JsonElement? graph = p.GraphJsonCache is not null
            ? JsonSerializer.Deserialize<JsonElement>(p.GraphJsonCache)
            : null;

        return new PipelineDto(
            p.Id,
            p.BroadcasterId.ToString(),
            p.Name,
            p.Description,
            p.IsEnabled,
            p.TriggerKind,
            graph,
            p.TriggerCount,
            p.LastTriggeredAt,
            p.CreatedAt,
            p.UpdatedAt
        );
    }
}
