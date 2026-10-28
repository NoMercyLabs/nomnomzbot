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

using NomNomzBot.Application.Commands.Dtos;
using NomNomzBot.Application.Common.Consequences;
using NomNomzBot.Application.Common.Models;

namespace NomNomzBot.Application.Commands.Services;

/// <summary>
/// Application service for managing custom chat commands.
/// </summary>
public interface ICommandService
{
    /// <summary>Create a new command in a channel.</summary>
    Task<Result<CommandDto>> CreateAsync(
        string broadcasterId,
        CreateCommandDto request,
        CancellationToken cancellationToken = default
    );

    /// <summary>Update an existing command.</summary>
    Task<Result<CommandDto>> UpdateAsync(
        string broadcasterId,
        string commandName,
        UpdateCommandDto request,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Delete a command by name. Destructive with no undo — when <paramref name="actorId"/> is supplied
    /// (the acting user's id), the deletion is recorded to the channel's audit trail before the row is
    /// removed, so the mod log can answer who deleted it and when.
    /// </summary>
    Task<Result> DeleteAsync(
        string broadcasterId,
        string commandName,
        string? actorId = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// What still runs this command, counted per surface (rewards, timers, event responses, chat triggers,
    /// giveaways, catalog items). The delete is refused while any of them is bound, so the dashboard shows
    /// this list before it asks.
    /// </summary>
    Task<Result<BlastRadiusDto>> GetDeleteBlastRadiusAsync(
        string broadcasterId,
        string commandName,
        CancellationToken cancellationToken = default
    );

    /// <summary>Get a single command by name.</summary>
    Task<Result<CommandDto>> GetAsync(
        string broadcasterId,
        string commandName,
        CancellationToken cancellationToken = default
    );

    /// <summary>List all commands in a channel with pagination.</summary>
    Task<Result<PagedList<CommandListItem>>> ListAsync(
        string broadcasterId,
        PaginationParams pagination,
        CancellationToken cancellationToken = default
    );
}
