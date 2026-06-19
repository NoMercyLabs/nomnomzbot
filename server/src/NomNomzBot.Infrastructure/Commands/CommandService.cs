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
using NomNomzBot.Application.Commands.Dtos;
using NomNomzBot.Application.Commands.Services;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Domain.Commands.Entities;

namespace NomNomzBot.Infrastructure.Commands;

public class CommandService : ICommandService
{
    private readonly IApplicationDbContext _db;

    public CommandService(IApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<Result<CommandDto>> CreateAsync(
        string broadcasterId,
        CreateCommandDto request,
        CancellationToken cancellationToken = default
    )
    {
        string normalizedName = request.Name.ToLowerInvariant();

        bool exists = await _db.Commands.AnyAsync(
            c => c.BroadcasterId == broadcasterId && c.Name == normalizedName,
            cancellationToken
        );

        if (exists)
            return Errors.AlreadyExists("command", request.Name).ToTyped<CommandDto>();

        Command command = new()
        {
            BroadcasterId = broadcasterId,
            Name = normalizedName,
            Type = request.Type,
            Permission = request.Permission,
            Response = request.Response,
            Responses = request.Responses ?? [],
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
        Command? command = await _db.Commands.FirstOrDefaultAsync(
            c => c.BroadcasterId == broadcasterId && c.Name == commandName,
            cancellationToken
        );

        if (command is null)
            return Errors.NotFound<CommandDto>("Command", commandName);

        if (request.Type is not null)
            command.Type = request.Type;
        if (request.Permission is not null)
            command.Permission = request.Permission;
        if (request.Response is not null)
            command.Response = request.Response;
        if (request.Responses is not null)
            command.Responses = request.Responses;
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
        Command? command = await _db.Commands.FirstOrDefaultAsync(
            c => c.BroadcasterId == broadcasterId && c.Name == commandName,
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
        Command? command = await _db.Commands.FirstOrDefaultAsync(
            c => c.BroadcasterId == broadcasterId && c.Name == commandName,
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
        IQueryable<Command> query = _db.Commands.Where(c => c.BroadcasterId == broadcasterId);
        int total = await query.CountAsync(cancellationToken);

        List<CommandListItem> items = await query
            .OrderBy(c => c.Name)
            .Skip((pagination.Page - 1) * pagination.PageSize)
            .Take(pagination.PageSize)
            .Select(c => new CommandListItem(
                c.Id,
                c.Name,
                c.Type,
                c.Permission,
                c.IsEnabled,
                c.CooldownSeconds,
                c.Description,
                c.Aliases,
                0,
                c.CreatedAt
            ))
            .ToListAsync(cancellationToken);

        return Result.Success(
            new PagedList<CommandListItem>(items, total, pagination.Page, pagination.PageSize)
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
        Command? command = await _db.Commands.FirstOrDefaultAsync(
            c => c.BroadcasterId == broadcasterId && c.Name == commandName && c.IsEnabled,
            cancellationToken
        );

        if (command is null)
            return Errors.NotFound<string>("Command", commandName);

        string? response =
            command.Response ?? (command.Responses.Count > 0 ? command.Responses[0] : null);

        return Result.Success(response ?? string.Empty);
    }

    private static CommandDto ToDto(Command c) =>
        new(
            c.Id,
            c.Name,
            c.Type,
            c.Permission,
            c.IsEnabled,
            c.Response,
            c.Responses,
            c.PipelineJson is not null
                ? System.Text.Json.JsonSerializer.Deserialize<object>(c.PipelineJson)
                : null,
            c.CooldownSeconds,
            c.CooldownPerUser,
            c.Description,
            c.Aliases,
            0,
            c.CreatedAt,
            c.UpdatedAt
        );
}
