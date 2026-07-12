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
using NomNomzBot.Application.PickLists.Dtos;

namespace NomNomzBot.Application.PickLists.Services;

/// <summary>
/// Manages a channel's generic named pick-lists — the reusable primitive behind the <c>{list.pick.&lt;name&gt;}</c>
/// template variable. Every read/write is scoped to a broadcaster and cross-tenant-safe (the global tenant
/// filter never blinds an operator acting on a channel they moderate).
/// </summary>
public interface IPickListService
{
    /// <summary>Lists the channel's pick-lists (by name), optionally filtered by a free-text term, paginated.</summary>
    Task<Result<PagedList<PickListDto>>> ListAsync(
        Guid broadcasterId,
        PickListSearch search,
        PaginationParams pagination,
        CancellationToken ct = default
    );

    /// <summary>Gets a pick-list by its id. Fails <c>NOT_FOUND</c> when absent.</summary>
    Task<Result<PickListDto>> GetAsync(Guid broadcasterId, Guid id, CancellationToken ct = default);

    /// <summary>Gets a pick-list by its unique name. Fails <c>NOT_FOUND</c> when absent.</summary>
    Task<Result<PickListDto>> GetByNameAsync(
        Guid broadcasterId,
        string name,
        CancellationToken ct = default
    );

    /// <summary>
    /// Creates a pick-list. Fails <c>ALREADY_EXISTS</c> if a live list of that name exists; a soft-deleted
    /// namesake is revived in place. Publishes <c>ChannelConfigChangedEvent</c>.
    /// </summary>
    Task<Result<PickListDto>> CreateAsync(
        Guid broadcasterId,
        CreatePickListRequest request,
        CancellationToken ct = default
    );

    /// <summary>Updates a pick-list's name/description/items. A rename that collides fails <c>ALREADY_EXISTS</c>.</summary>
    Task<Result<PickListDto>> UpdateAsync(
        Guid broadcasterId,
        Guid id,
        UpdatePickListRequest request,
        CancellationToken ct = default
    );

    /// <summary>Soft-deletes a pick-list; the name is freed for reuse. Publishes <c>ChannelConfigChangedEvent</c>.</summary>
    Task<Result> DeleteAsync(Guid broadcasterId, Guid id, CancellationToken ct = default);

    /// <summary>
    /// Returns a uniformly random entry from the named list. Fails <c>NOT_FOUND</c> when the list is missing and
    /// <c>PICKLIST_EMPTY</c> when it has no entries. This is the read the <c>{list.pick.&lt;name&gt;}</c> template
    /// variable rides on.
    /// </summary>
    Task<Result<string>> PickRandomAsync(
        Guid broadcasterId,
        string name,
        CancellationToken ct = default
    );
}
