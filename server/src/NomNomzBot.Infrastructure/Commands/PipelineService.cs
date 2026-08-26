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
using NomNomzBot.Domain.Commands.Entities;
using NomNomzBot.Domain.Platform.Events;
using NomNomzBot.Domain.Platform.Interfaces;
using PipelineEntity = NomNomzBot.Domain.Commands.Entities.Pipeline;

namespace NomNomzBot.Infrastructure.Commands;

public class PipelineService : IPipelineService
{
    private readonly IApplicationDbContext _db;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IEventBus _eventBus;
    private readonly ICommandConfigValidator _validator;
    private readonly IChannelRegistry _registry;

    public PipelineService(
        IApplicationDbContext db,
        IUnitOfWork unitOfWork,
        IEventBus eventBus,
        ICommandConfigValidator validator,
        IChannelRegistry registry
    )
    {
        _db = db;
        _unitOfWork = unitOfWork;
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

        // GraphJsonCache is only a performance cache — the normalized PipelineStep/PipelineStepCondition
        // rows are the execution truth PipelineEngine actually runs (LoadFromDbAsync, "DB steps take
        // priority over graph JSON cache"). A pipeline imported directly into those tables (e.g. an
        // old-bot migration) never gets a cache row written, so GraphJsonCache stays null while the
        // pipeline still executes — without this, the editor's GET returns nothing to render even
        // though the pipeline has real steps. Load and reconstruct from the rows whenever the cache is
        // absent so the editor always shows exactly what the engine would run.
        if (entity.GraphJsonCache is null)
        {
            List<PipelineStep> steps = await _db
                .PipelineSteps.Where(s => s.PipelineId == entity.Id)
                .Include(s => s.Conditions)
                .OrderBy(s => s.Order)
                .ToListAsync(ct);

            if (steps.Count > 0)
                return Result.Success(ToDto(entity, steps));
        }

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

        await _unitOfWork.ExecuteInTransactionAsync(
            async token =>
            {
                _db.Pipelines.Add(entity);
                await _db.SaveChangesAsync(token);
                await SyncStepRowsFromGraphAsync(
                    broadcaster,
                    entity.Id,
                    graphValidation.Value,
                    token
                );
                await _db.SaveChangesAsync(token);
            },
            ct
        );

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
        bool graphChanged = false;
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
            graphChanged = true;
        }

        await _unitOfWork.ExecuteInTransactionAsync(
            async token =>
            {
                await _db.SaveChangesAsync(token);
                if (graphChanged)
                {
                    await SyncStepRowsFromGraphAsync(
                        broadcaster,
                        entity.Id,
                        entity.GraphJsonCache,
                        token
                    );
                    await _db.SaveChangesAsync(token);
                }
            },
            ct
        );

        await PublishConfigChangedAsync(broadcaster, entity.Id, "updated", ct);
        await InvalidateBoundCachesAsync(broadcaster, ct);

        return Result.Success(ToDto(entity));
    }

    /// <summary>
    /// S-PIPE-WRITE-SYMMETRY: the engine (<see cref="Platform.Pipeline.PipelineEngine"/>,
    /// "Step source priority") executes a bound pipeline from its normalized
    /// <see cref="PipelineStep"/>/<see cref="PipelineStepCondition"/> rows FIRST, falling back to
    /// <c>GraphJsonCache</c> only when no rows exist — the rows, not the cache, are execution truth.
    /// <see cref="CreateAsync"/>/<see cref="UpdateAsync"/> used to write only the cache, leaving those
    /// rows permanently empty for every dashboard-authored pipeline; that asymmetry is what turned a
    /// wire-binding bug into unrecoverable data loss (both representations landed empty at once — see
    /// <c>PipelineServiceLegacyStepsTests</c>). This replaces the pipeline's full row set from the
    /// validated graph on every create/update so the two representations can never diverge — a
    /// hard-delete-and-reinsert rather than a diff, since a saved pipeline's step count/order is fully
    /// re-derived from the incoming graph each time (no orphan rows survive a removal or reorder).
    /// <see cref="PipelineStep"/>/<see cref="PipelineStepCondition"/> are plain <c>BaseEntity</c> rows
    /// (not soft-deletable), so a hard delete here is the correct lifecycle, matching how the engine's
    /// own <c>LoadFromDbAsync</c> reads them back.
    /// </summary>
    private async Task SyncStepRowsFromGraphAsync(
        Guid broadcasterId,
        Guid pipelineId,
        string? graphJson,
        CancellationToken ct
    )
    {
        List<PipelineStep> existingSteps = await _db
            .PipelineSteps.Where(s => s.PipelineId == pipelineId)
            .Include(s => s.Conditions)
            .ToListAsync(ct);

        foreach (PipelineStep existingStep in existingSteps)
        {
            if (existingStep.Conditions.Count > 0)
                _db.PipelineStepConditions.RemoveRange(existingStep.Conditions);
        }

        if (existingSteps.Count > 0)
            _db.PipelineSteps.RemoveRange(existingSteps);

        if (graphJson is null)
            return;

        PipelineDefinition? definition = JsonSerializer.Deserialize<PipelineDefinition>(graphJson);
        if (definition is null)
            return;

        for (int i = 0; i < definition.Steps.Count; i++)
        {
            PipelineStepDefinition stepDef = definition.Steps[i];
            PipelineStep step = new()
            {
                Id = Guid.NewGuid(),
                PipelineId = pipelineId,
                BroadcasterId = broadcasterId,
                Order = i,
                ActionType = stepDef.Action.Type,
                ConfigJson = JsonSerializer.Serialize(stepDef.Action),
                IsEnabled = true,
            };
            _db.PipelineSteps.Add(step);

            if (stepDef.Condition is not null)
            {
                _db.PipelineStepConditions.Add(
                    new PipelineStepCondition
                    {
                        Id = Guid.NewGuid(),
                        PipelineStepId = step.Id,
                        BroadcasterId = broadcasterId,
                        ConditionType = stepDef.Condition.Type,
                        Operator = stepDef.Condition.GetString("operator") ?? "eq",
                        LeftOperand = stepDef.Condition.GetString("left") ?? string.Empty,
                        RightOperand = stepDef.Condition.GetString("right") ?? string.Empty,
                        Negate = GetConditionBool(stepDef.Condition, "negate"),
                        Order = 0,
                    }
                );
            }
        }
    }

    private static bool GetConditionBool(ConditionDefinition condition, string key)
    {
        if (condition.Parameters is null)
            return false;
        if (!condition.Parameters.TryGetValue(key, out JsonElement elem))
            return false;
        return elem.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => false,
        };
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

    public async Task<Result<PipelineBlastRadiusDto>> GetBlastRadiusAsync(
        string broadcasterId,
        Guid id,
        CancellationToken ct = default
    )
    {
        if (!Guid.TryParse(broadcasterId, out Guid broadcaster))
            return Result<PipelineBlastRadiusDto>.Failure(
                $"Invalid channel ID '{broadcasterId}'.",
                "VALIDATION_FAILED"
            );

        bool exists = await _db.Pipelines.AnyAsync(
            p => p.BroadcasterId == broadcaster && p.Id == id,
            ct
        );
        if (!exists)
            return Result<PipelineBlastRadiusDto>.Failure(
                $"Pipeline '{id}' was not found.",
                "NOT_FOUND"
            );

        List<string> commandNames = await _db
            .Commands.Where(c => c.BroadcasterId == broadcaster && c.PipelineId == id)
            .Select(c => c.Name)
            .ToListAsync(ct);

        List<string> chatTriggerPatterns = await _db
            .ChatTriggers.Where(t => t.BroadcasterId == broadcaster && t.PipelineId == id)
            .Select(t => t.Pattern)
            .ToListAsync(ct);

        List<string> timerNames = await _db
            .Timers.Where(t => t.BroadcasterId == broadcaster && t.PipelineId == id)
            .Select(t => t.Name)
            .ToListAsync(ct);

        List<string> eventResponseEventTypes = await _db
            .EventResponses.Where(r => r.BroadcasterId == broadcaster && r.PipelineId == id)
            .Select(r => r.EventType)
            .ToListAsync(ct);

        return Result<PipelineBlastRadiusDto>.Success(
            new PipelineBlastRadiusDto(
                commandNames.Count,
                commandNames,
                chatTriggerPatterns.Count,
                chatTriggerPatterns,
                timerNames.Count,
                timerNames,
                eventResponseEventTypes.Count,
                eventResponseEventTypes
            )
        );
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

        return ToDto(p, graph);
    }

    /// <summary>Builds the DTO with its graph reconstructed from normalized <see cref="PipelineStep"/> rows,
    /// in the exact wire shape <see cref="PipelineDefinition"/> reads back (<c>steps[].action</c> /
    /// <c>steps[].condition</c>) — the same shape the editor's builder + <see cref="ValidateAndSerializeGraphAsync"/>
    /// already speak.</summary>
    private static PipelineDto ToDto(PipelineEntity p, List<PipelineStep> steps) =>
        ToDto(p, BuildGraphFromSteps(steps));

    private static JsonElement BuildGraphFromSteps(List<PipelineStep> steps)
    {
        List<object> stepNodes = [];
        foreach (PipelineStep step in steps)
        {
            JsonElement actionJson;
            try
            {
                using JsonDocument doc = JsonDocument.Parse(step.ConfigJson);
                Dictionary<string, JsonElement> actionFields = doc
                    .RootElement.EnumerateObject()
                    .Where(prop =>
                        !string.Equals(prop.Name, "type", StringComparison.OrdinalIgnoreCase)
                    )
                    .ToDictionary(prop => prop.Name, prop => prop.Value.Clone());
                actionFields["type"] = JsonSerializer.SerializeToElement(step.ActionType);
                actionJson = JsonSerializer.SerializeToElement(actionFields);
            }
            catch (JsonException)
            {
                actionJson = JsonSerializer.SerializeToElement(new { type = step.ActionType });
            }

            PipelineStepCondition? firstCondition = step
                .Conditions?.OrderBy(c => c.Order)
                .FirstOrDefault();

            object? conditionNode = firstCondition is null
                ? null
                : new
                {
                    type = firstCondition.ConditionType,
                    @operator = firstCondition.Operator ?? "eq",
                    left = firstCondition.LeftOperand ?? string.Empty,
                    right = firstCondition.RightOperand ?? string.Empty,
                    negate = firstCondition.Negate,
                };

            stepNodes.Add(new { action = actionJson, condition = conditionNode });
        }

        return JsonSerializer.SerializeToElement(new { steps = stepNodes });
    }

    private static PipelineDto ToDto(PipelineEntity p, JsonElement? graph) =>
        new(
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
