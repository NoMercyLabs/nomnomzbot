// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Commands.Dtos;
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

    /// <summary>Delete a command by name.</summary>
    Task<Result> DeleteAsync(
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

    /// <summary>Execute a command and return its response text.</summary>
    Task<Result<string>> ExecuteAsync(
        string broadcasterId,
        string commandName,
        string userId,
        string? input = null,
        CancellationToken cancellationToken = default
    );
}
