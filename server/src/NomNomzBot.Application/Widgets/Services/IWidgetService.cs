// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Widgets.Dtos;

namespace NomNomzBot.Application.Widgets.Services;

/// <summary>
/// Application service for managing overlay widgets (alerts, chat overlays, goals, etc.).
/// </summary>
public interface IWidgetService
{
    /// <summary>Create a new widget.</summary>
    Task<Result<WidgetDetail>> CreateAsync(
        string broadcasterId,
        CreateWidgetRequest request,
        CancellationToken cancellationToken = default
    );

    /// <summary>Update an existing widget.</summary>
    Task<Result<WidgetDetail>> UpdateAsync(
        string broadcasterId,
        string widgetId,
        UpdateWidgetRequest request,
        CancellationToken cancellationToken = default
    );

    /// <summary>Delete a widget.</summary>
    Task<Result> DeleteAsync(
        string broadcasterId,
        string widgetId,
        CancellationToken cancellationToken = default
    );

    /// <summary>List all widgets for a channel with pagination.</summary>
    Task<Result<PagedList<WidgetDetail>>> ListAsync(
        string broadcasterId,
        PaginationParams pagination,
        CancellationToken cancellationToken = default
    );

    /// <summary>Get a single widget by ID.</summary>
    Task<Result<WidgetDetail>> GetAsync(
        string broadcasterId,
        string widgetId,
        CancellationToken cancellationToken = default
    );

    /// <summary>Get a widget by its public access token (for overlay URLs).</summary>
    Task<Result<WidgetDetail>> GetByTokenAsync(
        string token,
        CancellationToken cancellationToken = default
    );
}
