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
using NomNomzBot.Domain.Commands.Entities;

namespace NomNomzBot.Infrastructure.Commands;

public class CommandService : ICommandService
{
    private readonly IApplicationDbContext _db;
    private readonly IPipelineEngine _pipelineEngine;

    public CommandService(IApplicationDbContext db, IPipelineEngine pipelineEngine)
    {
        _db = db;
        _pipelineEngine = pipelineEngine;
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

        Command command = new()
        {
            BroadcasterId = broadcaster,
            Name = request.Name,
            NameNormalized = nameNormalized,
            Tier = request.Tier,
            MinPermissionLevel = request.MinPermissionLevel,
            TemplateResponse = request.TemplateResponse,
            TemplateResponses = request.TemplateResponses ?? [],
            PipelineId = request.PipelineId,
            CooldownSeconds = request.CooldownSeconds,
            CooldownPerUser = request.CooldownPerUser,
            Description = request.Description,
            Aliases = request.Aliases ?? [],
            IsEnabled = true,
        };

        _db.Commands.Add(command);
        await _db.SaveChangesAsync(cancellationToken);

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
        if (request.TemplateResponse is not null)
            command.TemplateResponse = request.TemplateResponse;
        if (request.TemplateResponses is not null)
            command.TemplateResponses = request.TemplateResponses;
        if (request.PipelineId.HasValue)
            command.PipelineId = request.PipelineId.Value;
        if (request.CooldownSeconds.HasValue)
            command.CooldownSeconds = request.CooldownSeconds.Value;
        if (request.CooldownPerUser.HasValue)
            command.CooldownPerUser = request.CooldownPerUser.Value;
        if (request.Description is not null)
            command.Description = request.Description;
        if (request.Aliases is not null)
            command.Aliases = request.Aliases;
        if (request.IsEnabled.HasValue)
            command.IsEnabled = request.IsEnabled.Value;

        await _db.SaveChangesAsync(cancellationToken);

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

        _db.Commands.Remove(command);
        await _db.SaveChangesAsync(cancellationToken);

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
                c.CooldownSeconds,
                c.Description,
                c.Aliases,
                c.UseCount,
                c.CreatedAt
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

    private static CommandDto ToDto(Command c) =>
        new(
            c.Id,
            c.Name,
            c.Tier,
            c.MinPermissionLevel,
            c.IsEnabled,
            c.TemplateResponse,
            c.TemplateResponses,
            c.PipelineId,
            c.CooldownSeconds,
            c.CooldownPerUser,
            c.Description,
            c.Aliases,
            c.UseCount,
            c.CreatedAt,
            c.UpdatedAt
        );
}
