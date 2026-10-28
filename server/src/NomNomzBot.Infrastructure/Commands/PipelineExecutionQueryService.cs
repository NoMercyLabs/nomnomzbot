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
using NomNomzBot.Application.Commands.Dtos;
using NomNomzBot.Application.Commands.Services;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Domain.Commands.Entities;

namespace NomNomzBot.Infrastructure.Commands;

public class PipelineExecutionQueryService : IPipelineExecutionQueryService
{
    private readonly IApplicationDbContext _db;

    /// <summary>Non-success outcomes, mirroring <c>PipelineEngine.ToStatus</c> (Platform/Pipeline/PipelineEngine.cs).</summary>
    private static readonly HashSet<string> FailureStatuses =
    [
        "failed",
        "partially_failed",
        "timed_out",
        "cancelled",
    ];

    public PipelineExecutionQueryService(IApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<Result<PagedList<PipelineExecutionSummaryDto>>> ListAsync(
        string broadcasterId,
        PaginationParams pagination,
        bool failuresOnly,
        Guid? pipelineId = null,
        CancellationToken ct = default
    )
    {
        if (!Guid.TryParse(broadcasterId, out Guid broadcaster))
            return Result.Failure<PagedList<PipelineExecutionSummaryDto>>(
                $"Invalid channel ID '{broadcasterId}'.",
                "VALIDATION_FAILED"
            );

        IQueryable<PipelineExecution> query = _db.PipelineExecutions.Where(e =>
            e.BroadcasterId == broadcaster
        );

        if (failuresOnly)
            query = query.Where(e => FailureStatuses.Contains(e.Status));

        if (pipelineId.HasValue)
            query = query.Where(e => e.PipelineId == pipelineId.Value);

        int total = await query.CountAsync(ct);

        List<PipelineExecutionSummaryDto> items = await query
            .OrderByDescending(e => e.StartedAt)
            .ThenByDescending(e => e.Id)
            .Skip((pagination.Page - 1) * pagination.PageSize)
            .Take(pagination.PageSize)
            .Select(e => new PipelineExecutionSummaryDto(
                e.Id,
                e.PipelineId,
                e.TriggerKind,
                e.Status,
                e.HostCallCount,
                e.DurationMs,
                e.ErrorMessage,
                e.StartedAt,
                e.CompletedAt
            ))
            .ToListAsync(ct);

        return Result.Success(
            new PagedList<PipelineExecutionSummaryDto>(
                items,
                pagination.Page,
                pagination.PageSize,
                total
            )
        );
    }

    public async Task<Result<PipelineExecutionDetailDto>> GetDetailAsync(
        string broadcasterId,
        long id,
        CancellationToken ct = default
    )
    {
        if (!Guid.TryParse(broadcasterId, out Guid broadcaster))
            return Result.Failure<PipelineExecutionDetailDto>(
                $"Invalid channel ID '{broadcasterId}'.",
                "VALIDATION_FAILED"
            );

        PipelineExecution? entity = await _db.PipelineExecutions.FirstOrDefaultAsync(
            e => e.BroadcasterId == broadcaster && e.Id == id,
            ct
        );

        if (entity is null)
            return Errors.NotFound<PipelineExecutionDetailDto>("PipelineExecution", id.ToString());

        return Result.Success(ToDetailDto(entity));
    }

    /// <summary>Mirrors the wire shape <c>PipelineEngine.PersistExecutionAsync</c> serializes onto
    /// <see cref="PipelineExecution.StepLogsJson"/> (StepIndex, ActionType, Succeeded, DurationMs, ErrorMessage).</summary>
    private sealed record StepLogJson(
        int StepIndex,
        string ActionType,
        bool Succeeded,
        int DurationMs,
        string? ErrorMessage
    );

    private static PipelineExecutionDetailDto ToDetailDto(PipelineExecution entity)
    {
        List<StepLogJson> raw = entity.StepLogsJson is null
            ? []
            : JsonSerializer.Deserialize<List<StepLogJson>>(entity.StepLogsJson) ?? [];

        List<PipelineExecutionStepLogDto> stepLogs =
        [
            .. raw.Select(s => new PipelineExecutionStepLogDto(
                s.StepIndex,
                s.ActionType,
                s.Succeeded,
                s.DurationMs,
                s.ErrorMessage
            )),
        ];

        return new(
            entity.Id,
            entity.PipelineId,
            entity.TriggerKind,
            entity.Status,
            entity.HostCallCount,
            entity.DurationMs,
            entity.ErrorMessage,
            entity.StartedAt,
            entity.CompletedAt,
            stepLogs
        );
    }
}
