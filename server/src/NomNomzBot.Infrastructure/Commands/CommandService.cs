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
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Commands.Dtos;
using NomNomzBot.Application.Commands.Services;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Billing;
using NomNomzBot.Domain.Commands.Entities;
using NomNomzBot.Domain.Platform.Events;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Infrastructure.Commands;

public class CommandService : ICommandService
{
    private readonly IApplicationDbContext _db;
    private readonly IPipelineEngine _pipelineEngine;
    private readonly IChannelRegistry _registry;
    private readonly IEventBus _eventBus;
    private readonly IBillingTierService _tiers;

    public CommandService(
        IApplicationDbContext db,
        IPipelineEngine pipelineEngine,
        IChannelRegistry registry,
        IEventBus eventBus,
        IBillingTierService tiers
    )
    {
        _db = db;
        _pipelineEngine = pipelineEngine;
        _registry = registry;
        _eventBus = eventBus;
        _tiers = tiers;
    }

    public async Task<Result<CommandDto>> CreateAsync(
        string broadcasterId,
        CreateCommandDto request,
        CancellationToken cancellationToken = default
    )
    {
        if (!Guid.TryParse(broadcasterId, out Guid broadcaster))
            return Result.Failure<CommandDto>(
                $"Invalid channel ID '{broadcasterId}'.",
                "VALIDATION_FAILED"
            );

        string nameNormalized = request.Name.ToLowerInvariant();

        bool exists = await _db.Commands.AnyAsync(
            c => c.BroadcasterId == broadcaster && c.NameNormalized == nameNormalized,
            cancellationToken
        );

        if (exists)
            return Errors.AlreadyExists("command", request.Name).ToTyped<CommandDto>();

        // Tier quotas (monetization-billing §3.3): the command count and the per-trigger variation list
        // are both capped by the plan; -1 (self-host / unseeded) is unlimited.
        Result<long> commandCap = await _tiers.GetLimitAsync(
            broadcaster,
            "custom_commands",
            cancellationToken
        );
        if (commandCap is { IsSuccess: true, Value: >= 0 })
        {
            int current = await _db.Commands.CountAsync(
                c => c.BroadcasterId == broadcaster,
                cancellationToken
            );
            if (current >= commandCap.Value)
                return Errors
                    .QuotaExceeded("custom commands", commandCap.Value)
                    .ToTyped<CommandDto>();
        }

        Result variationsOk = await CheckVariationCapAsync(
            broadcaster,
            request.TemplateResponses?.Count ?? 0,
            cancellationToken
        );
        if (variationsOk.IsFailure)
            return variationsOk.ToTyped<CommandDto>();

        Command command = new()
        {
            BroadcasterId = broadcaster,
            Name = request.Name,
            NameNormalized = nameNormalized,
            Tier = request.Tier,
            MinPermissionLevel = request.MinPermissionLevel,
            PrefixMode = request.PrefixMode,
            CustomPrefix = request.CustomPrefix,
            MatchMode = request.MatchMode,
            MatchPattern = request.MatchPattern,
            TemplateResponse = request.TemplateResponse,
            TemplateResponses = request.TemplateResponses ?? [],
            PipelineId = request.PipelineId,
            CooldownSeconds = request.CooldownSeconds,
            UserCooldownSeconds = request.UserCooldownSeconds,
            CooldownPerUser = request.CooldownPerUser,
            Description = request.Description,
            Aliases = request.Aliases ?? [],
            IsEnabled = request.IsEnabled,
        };

        _db.Commands.Add(command);
        await _db.SaveChangesAsync(cancellationToken);
        await _registry.InvalidateCommandsAsync(broadcaster, cancellationToken);
        await PublishConfigChangedAsync(broadcaster, command.Id, "created", cancellationToken);

        return Result.Success(ToDto(command));
    }

    public async Task<Result<CommandDto>> UpdateAsync(
        string broadcasterId,
        string commandName,
        UpdateCommandDto request,
        CancellationToken cancellationToken = default
    )
    {
        if (!Guid.TryParse(broadcasterId, out Guid broadcaster))
            return Result.Failure<CommandDto>(
                $"Invalid channel ID '{broadcasterId}'.",
                "VALIDATION_FAILED"
            );

        string nameNormalized = commandName.ToLowerInvariant();

        Command? command = await _db.Commands.FirstOrDefaultAsync(
            c => c.BroadcasterId == broadcaster && c.NameNormalized == nameNormalized,
            cancellationToken
        );

        if (command is null)
            return Errors.NotFound<CommandDto>("Command", commandName);

        if (request.Tier is not null)
            command.Tier = request.Tier;
        if (request.MinPermissionLevel.HasValue)
            command.MinPermissionLevel = request.MinPermissionLevel.Value;
        if (request.PrefixMode is not null)
            command.PrefixMode = request.PrefixMode;
        if (request.CustomPrefix is not null)
            command.CustomPrefix = request.CustomPrefix.Length == 0 ? null : request.CustomPrefix;
        if (request.MatchMode is not null)
            command.MatchMode = request.MatchMode;
        if (request.MatchPattern is not null)
            command.MatchPattern = request.MatchPattern.Length == 0 ? null : request.MatchPattern;
        if (request.TemplateResponse is not null)
            command.TemplateResponse = request.TemplateResponse;
        if (request.TemplateResponses is not null)
        {
            Result variationsOk = await CheckVariationCapAsync(
                broadcaster,
                request.TemplateResponses.Count,
                cancellationToken
            );
            if (variationsOk.IsFailure)
                return variationsOk.ToTyped<CommandDto>();
            command.TemplateResponses = request.TemplateResponses;
        }
        if (request.PipelineId.HasValue)
            command.PipelineId = request.PipelineId.Value;
        if (request.CooldownSeconds.HasValue)
            command.CooldownSeconds = request.CooldownSeconds.Value;
        if (request.UserCooldownSeconds.HasValue)
            command.UserCooldownSeconds = request.UserCooldownSeconds.Value;
        if (request.CooldownPerUser.HasValue)
            command.CooldownPerUser = request.CooldownPerUser.Value;
        if (request.Description is not null)
            command.Description = request.Description;
        if (request.Aliases is not null)
            command.Aliases = request.Aliases;
        if (request.IsEnabled.HasValue)
            command.IsEnabled = request.IsEnabled.Value;

        await _db.SaveChangesAsync(cancellationToken);
        await _registry.InvalidateCommandsAsync(broadcaster, cancellationToken);
        await PublishConfigChangedAsync(broadcaster, command.Id, "updated", cancellationToken);

        return Result.Success(ToDto(command));
    }

    public async Task<Result> DeleteAsync(
        string broadcasterId,
        string commandName,
        CancellationToken cancellationToken = default
    )
    {
        if (!Guid.TryParse(broadcasterId, out Guid broadcaster))
            return Result.Failure($"Invalid channel ID '{broadcasterId}'.", "VALIDATION_FAILED");

        string nameNormalized = commandName.ToLowerInvariant();

        Command? command = await _db.Commands.FirstOrDefaultAsync(
            c => c.BroadcasterId == broadcaster && c.NameNormalized == nameNormalized,
            cancellationToken
        );

        if (command is null)
            return Result.Failure($"Command '{commandName}' was not found.", "NOT_FOUND");

        Guid commandId = command.Id;
        _db.Commands.Remove(command);
        await _db.SaveChangesAsync(cancellationToken);
        await _registry.InvalidateCommandsAsync(broadcaster, cancellationToken);
        await PublishConfigChangedAsync(broadcaster, commandId, "deleted", cancellationToken);

        return Result.Success();
    }

    public async Task<Result<CommandDto>> GetAsync(
        string broadcasterId,
        string commandName,
        CancellationToken cancellationToken = default
    )
    {
        if (!Guid.TryParse(broadcasterId, out Guid broadcaster))
            return Result.Failure<CommandDto>(
                $"Invalid channel ID '{broadcasterId}'.",
                "VALIDATION_FAILED"
            );

        string nameNormalized = commandName.ToLowerInvariant();

        Command? command = await _db.Commands.FirstOrDefaultAsync(
            c => c.BroadcasterId == broadcaster && c.NameNormalized == nameNormalized,
            cancellationToken
        );

        if (command is null)
            return Errors.NotFound<CommandDto>("Command", commandName);

        return Result.Success(ToDto(command));
    }

    public async Task<Result<PagedList<CommandListItem>>> ListAsync(
        string broadcasterId,
        PaginationParams pagination,
        CancellationToken cancellationToken = default
    )
    {
        if (!Guid.TryParse(broadcasterId, out Guid broadcaster))
            return Result.Failure<PagedList<CommandListItem>>(
                $"Invalid channel ID '{broadcasterId}'.",
                "VALIDATION_FAILED"
            );

        IQueryable<Command> query = _db.Commands.Where(c => c.BroadcasterId == broadcaster);
        int total = await query.CountAsync(cancellationToken);

        List<CommandListItem> items = await query
            .OrderBy(c => c.Name)
            .Skip((pagination.Page - 1) * pagination.PageSize)
            .Take(pagination.PageSize)
            .Select(c => new CommandListItem(
                c.Id,
                c.Name,
                c.Tier,
                c.MinPermissionLevel,
                c.IsEnabled,
                c.PrefixMode,
                c.CustomPrefix,
                c.MatchMode,
                c.MatchPattern,
                c.CooldownSeconds,
                c.UserCooldownSeconds,
                c.CooldownPerUser,
                c.Description,
                c.Aliases,
                c.UseCount,
                c.CreatedAt,
                c.TemplateResponse,
                c.TemplateResponses,
                c.PipelineId
            ))
            .ToListAsync(cancellationToken);

        return Result.Success(
            new PagedList<CommandListItem>(items, pagination.Page, pagination.PageSize, total)
        );
    }

    public async Task<Result<string>> ExecuteAsync(
        string broadcasterId,
        string commandName,
        string userId,
        string? input = null,
        CancellationToken cancellationToken = default
    )
    {
        if (!Guid.TryParse(broadcasterId, out Guid broadcaster))
            return Result.Failure<string>(
                $"Invalid channel ID '{broadcasterId}'.",
                "VALIDATION_FAILED"
            );

        string nameNormalized = commandName.ToLowerInvariant();

        Command? command = await _db.Commands.FirstOrDefaultAsync(
            c =>
                c.BroadcasterId == broadcaster && c.NameNormalized == nameNormalized && c.IsEnabled,
            cancellationToken
        );

        if (command is null)
            return Errors.NotFound<string>("Command", commandName);

        if (command.Tier == "pipeline" && command.PipelineId.HasValue)
        {
            // Load the pipeline's graph cache to drive the engine (steps-first engine is Slice 4).
            Pipeline? pipeline = await _db.Pipelines.FirstOrDefaultAsync(
                p => p.Id == command.PipelineId.Value,
                cancellationToken
            );

            string graphJson = pipeline?.GraphJsonCache ?? "{}";

            PipelineRequest pipelineRequest = new()
            {
                BroadcasterId = broadcaster,
                PipelineId = command.PipelineId,
                PipelineJson = graphJson,
                TriggeredByUserId = userId,
                TriggeredByDisplayName = userId,
                RawMessage = input ?? string.Empty,
            };

            PipelineExecutionResult execResult = await _pipelineEngine.ExecuteAsync(
                pipelineRequest,
                cancellationToken
            );

            return Result.Success(execResult.Outcome.ToString());
        }

        // Template tier: pick a response.
        string? response =
            command.TemplateResponse
            ?? (command.TemplateResponses is { Count: > 0 } ? command.TemplateResponses[0] : null);

        return Result.Success(response ?? string.Empty);
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
            ? Errors.QuotaExceeded("response variations per command", cap.Value)
            : Result.Success();
    }

    /// <summary>E5 dashboard live-sync: fired after every successful write so other open dashboards refetch.</summary>
    private Task PublishConfigChangedAsync(
        Guid broadcasterId,
        Guid commandId,
        string action,
        CancellationToken cancellationToken
    ) =>
        _eventBus.PublishAsync(
            new ChannelConfigChangedEvent
            {
                BroadcasterId = broadcasterId,
                Domain = "commands",
                EntityId = commandId.ToString(),
                Action = action,
            },
            cancellationToken
        );

    private static CommandDto ToDto(Command c) =>
        new(
            c.Id,
            c.Name,
            c.Tier,
            c.MinPermissionLevel,
            c.IsEnabled,
            c.PrefixMode,
            c.CustomPrefix,
            c.MatchMode,
            c.MatchPattern,
            c.TemplateResponse,
            c.TemplateResponses,
            c.PipelineId,
            c.CooldownSeconds,
            c.UserCooldownSeconds,
            c.CooldownPerUser,
            c.Description,
            c.Aliases,
            c.UseCount,
            c.CreatedAt,
            c.UpdatedAt
        );
}
