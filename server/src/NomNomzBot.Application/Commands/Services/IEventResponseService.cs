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
/// Manages per-channel event response configurations.
/// </summary>
public interface IEventResponseService
{
    Task<Result<PagedList<EventResponseListItem>>> ListAsync(
        string broadcasterId,
        PaginationParams pagination,
        CancellationToken cancellationToken = default
    );
    Task<Result<EventResponseDto>> GetByEventTypeAsync(
        string broadcasterId,
        string eventType,
        CancellationToken cancellationToken = default
    );
    Task<Result<EventResponseDto>> UpsertAsync(
        string broadcasterId,
        string eventType,
        UpdateEventResponseDto request,
        CancellationToken cancellationToken = default
    );
    Task<Result> DeleteAsync(
        string broadcasterId,
        string eventType,
        CancellationToken cancellationToken = default
    );
}
