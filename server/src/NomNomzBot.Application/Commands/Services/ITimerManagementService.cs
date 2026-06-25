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
/// Application service for managing per-channel message timers via the REST API.
/// </summary>
public interface ITimerManagementService
{
    /// <summary>List all timers for a channel.</summary>
    Task<Result<PagedList<TimerListItem>>> ListAsync(
        string broadcasterId,
        PaginationParams pagination,
        CancellationToken cancellationToken = default
    );

    /// <summary>Get a single timer by ID.</summary>
    Task<Result<TimerDto>> GetAsync(
        string broadcasterId,
        Guid id,
        CancellationToken cancellationToken = default
    );

    /// <summary>Create a new timer.</summary>
    Task<Result<TimerDto>> CreateAsync(
        string broadcasterId,
        CreateTimerDto request,
        CancellationToken cancellationToken = default
    );

    /// <summary>Update an existing timer.</summary>
    Task<Result<TimerDto>> UpdateAsync(
        string broadcasterId,
        Guid id,
        UpdateTimerDto request,
        CancellationToken cancellationToken = default
    );

    /// <summary>Delete a timer.</summary>
    Task<Result> DeleteAsync(
        string broadcasterId,
        Guid id,
        CancellationToken cancellationToken = default
    );

    /// <summary>Toggle a timer enabled/disabled.</summary>
    Task<Result<TimerDto>> ToggleAsync(
        string broadcasterId,
        Guid id,
        CancellationToken cancellationToken = default
    );
}
