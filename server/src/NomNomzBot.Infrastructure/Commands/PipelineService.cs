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
using PipelineEntity = NomNomzBot.Domain.Commands.Entities.Pipeline;

namespace NomNomzBot.Infrastructure.Commands;

public class PipelineService : IPipelineService
{
    private readonly IApplicationDbContext _db;

    public PipelineService(IApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<Result<PagedList<PipelineListItemDto>>> ListAsync(
        string broadcasterId,
        PaginationParams pagination,
        CancellationToken ct = default
    )
    {
        IQueryable<PipelineEntity> query = _db.Pipelines.Where(p =>
            p.BroadcasterId == broadcasterId
        );
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
            new PagedList<PipelineListItemDto>(items, total, pagination.Page, pagination.PageSize)
        );
    }

    public async Task<Result<PipelineDto>> GetAsync(
        string broadcasterId,
        int id,
        CancellationToken ct = default
    )
    {
        PipelineEntity? entity = await _db.Pipelines.FirstOrDefaultAsync(
            p => p.BroadcasterId == broadcasterId && p.Id == id,
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
        PipelineEntity entity = new()
        {
            BroadcasterId = broadcasterId,
            Name = request.Name,
            Description = request.Description,
            IsEnabled = request.IsEnabled,
            GraphJson = request.Graph is not null ? JsonSerializer.Serialize(request.Graph) : "{}",
        };

        _db.Pipelines.Add(entity);
        await _db.SaveChangesAsync(ct);

        return Result.Success(ToDto(entity));
    }

    public async Task<Result<PipelineDto>> UpdateAsync(
        string broadcasterId,
        int id,
        UpdatePipelineDto request,
        CancellationToken ct = default
    )
    {
        PipelineEntity? entity = await _db.Pipelines.FirstOrDefaultAsync(
            p => p.BroadcasterId == broadcasterId && p.Id == id,
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
        if (request.Graph is not null)
            entity.GraphJson = JsonSerializer.Serialize(request.Graph);

        await _db.SaveChangesAsync(ct);

        return Result.Success(ToDto(entity));
    }

    public async Task<Result> DeleteAsync(
        string broadcasterId,
        int id,
        CancellationToken ct = default
    )
    {
        PipelineEntity? entity = await _db.Pipelines.FirstOrDefaultAsync(
            p => p.BroadcasterId == broadcasterId && p.Id == id,
            ct
        );

        if (entity is null)
            return Result.Failure($"Pipeline '{id}' was not found.", "NOT_FOUND");

        _db.Pipelines.Remove(entity);
        await _db.SaveChangesAsync(ct);

        return Result.Success();
    }

    private static PipelineDto ToDto(PipelineEntity p) =>
        new(
            p.Id,
            p.BroadcasterId,
            p.Name,
            p.Description,
            p.IsEnabled,
            JsonSerializer.Deserialize<JsonElement>(p.GraphJson),
            p.TriggerCount,
            p.LastTriggeredAt,
            p.CreatedAt,
            p.UpdatedAt
        );
}
