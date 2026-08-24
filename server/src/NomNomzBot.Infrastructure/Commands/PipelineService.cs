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
using NomNomzBot.Application.Abstractions.Pipeline;
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
    private readonly ICommandConfigValidator _validator;
    private readonly IChannelRegistry _registry;

    public PipelineService(
        IApplicationDbContext db,
        IEventBus eventBus,
        ICommandConfigValidator validator,
        IChannelRegistry registry
    )
    {
        _db = db;
        _eventBus = eventBus;
        _validator = validator;
        _registry = registry;
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

        Result<string?> graphValidation = await ValidateAndSerializeGraphAsync(
            request.GraphJsonCache,
            ct
        );
        if (!graphValidation.IsSuccess)
            return Result.Failure<PipelineDto>(
                graphValidation.ErrorMessage,
                graphValidation.ErrorCode
            );

        PipelineEntity entity = new()
        {
            BroadcasterId = broadcaster,
            Name = request.Name,
            Description = request.Description,
            IsEnabled = request.IsEnabled,
            TriggerKind = request.TriggerKind,
            GraphJsonCache = graphValidation.Value,
        };

        _db.Pipelines.Add(entity);
        await _db.SaveChangesAsync(ct);
        await PublishConfigChangedAsync(broadcaster, entity.Id, "created", ct);
        await InvalidateBoundCachesAsync(broadcaster, ct);

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
        {
            Result<string?> graphValidation = await ValidateAndSerializeGraphAsync(
                request.GraphJsonCache,
                ct
            );
            if (!graphValidation.IsSuccess)
                return Result.Failure<PipelineDto>(
                    graphValidation.ErrorMessage,
                    graphValidation.ErrorCode
                );

            entity.GraphJsonCache = graphValidation.Value;
        }

        await _db.SaveChangesAsync(ct);
        await PublishConfigChangedAsync(broadcaster, entity.Id, "updated", ct);
        await InvalidateBoundCachesAsync(broadcaster, ct);

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
        await InvalidateBoundCachesAsync(broadcaster, ct);

        return Result.Success();
    }

    /// <summary>
    /// A pipeline's steps are only ever reached through a bound <c>Command</c> or <c>ChatTrigger</c>, and both
    /// resolve the pipeline's <c>GraphJsonCache</c> once, at cache-load time, into <see cref="Domain.Platform.Interfaces.CachedCommand.PipelineGraphJson"/>
    /// / <see cref="Domain.Platform.Interfaces.CachedChatTrigger.PipelineGraphJson"/> — so a create, edit or
    /// delete here has to force BOTH hot-path caches to reload, or a bound command/trigger keeps running the old
    /// graph (edit) or a graph that no longer exists (delete) until the process restarts. Scoped to this one
    /// broadcaster's <see cref="ChannelContext"/> only — <see cref="IChannelRegistry"/>'s invalidation methods
    /// are a no-op for any channel that isn't currently registered, and never touch another tenant's entry.
    /// </summary>
    private async Task InvalidateBoundCachesAsync(Guid broadcasterId, CancellationToken ct)
    {
        await _registry.InvalidateCommandsAsync(broadcasterId, ct);
        await _registry.InvalidateChatTriggersAsync(broadcasterId, ct);
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

    /// <summary>
    /// Save-time gate (S007): re-serializes the incoming graph to its exact wire JSON, deserializes it as
    /// the same <see cref="PipelineDefinition"/> shape <see cref="Platform.Pipeline.PipelineEngine"/> reads
    /// at execution time, and runs it through the existing <see cref="ICommandConfigValidator"/> — the same
    /// rules the optional "validate" editor endpoint enforces. Any client that skips that endpoint (import,
    /// automation, direct API) is now blocked here instead of failing live in front of viewers.
    /// Returns the exact JSON to persist on success, or a typed failure that leaves the caller's existing
    /// stored graph untouched.
    /// </summary>
    private async Task<Result<string?>> ValidateAndSerializeGraphAsync(
        object? graphJsonCache,
        CancellationToken ct
    )
    {
        if (graphJsonCache is null)
            return Result.Success<string?>(null);

        string rawJson = JsonSerializer.Serialize(graphJsonCache);

        PipelineDefinition? definition;
        try
        {
            definition = JsonSerializer.Deserialize<PipelineDefinition>(rawJson);
        }
        catch (JsonException ex)
        {
            return Result.Failure<string?>(
                $"Pipeline graph is not valid JSON: {ex.Message}",
                "INVALID_GRAPH"
            );
        }

        Result<PipelineValidationResult> validation = await _validator.ValidatePipelineAsync(
            ToValidatorInput(definition ?? new PipelineDefinition()),
            ct
        );

        if (!validation.IsSuccess)
            return Result.Failure<string?>(validation.ErrorMessage, validation.ErrorCode);

        if (!validation.Value.IsValid)
            return Result.Failure<string?>(
                validation.Value.ErrorMessage,
                validation.Value.ErrorCode
            );

        return Result.Success<string?>(rawJson);
    }

    /// <summary>Maps the engine's action/condition graph shape onto the validator's input contract.</summary>
    private static PipelineGraphInput ToValidatorInput(PipelineDefinition definition) =>
        new([
            .. definition.Steps.Select(step => new PipelineStepInput(
                step.Action.Type,
                step.Action.Parameters?.ToDictionary(kv => kv.Key, kv => (object?)kv.Value)
                    ?? new Dictionary<string, object?>(),
                step.Condition?.Type,
                step.Condition?.Parameters?.ToDictionary(kv => kv.Key, kv => (object?)kv.Value)
            )),
        ]);

    private static PipelineDto ToDto(PipelineEntity p)
    {
        JsonElement? graph = p.GraphJsonCache is not null
            ? JsonSerializer.Deserialize<JsonElement>(p.GraphJsonCache)
            : null;

        return new(
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
