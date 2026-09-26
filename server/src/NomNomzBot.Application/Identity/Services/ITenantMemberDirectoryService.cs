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
using NomNomzBot.Application.Identity.Dtos;

namespace NomNomzBot.Application.Identity.Services;

/// <summary>
/// Who belongs to one tenant, read across the tenant filter: the owner, every live management membership,
/// every community standing, and every viewer the channel has a profile for. This is a plain data read with no
/// authorization of its own; the platform-admin surface that calls it gates and audits the call.
/// </summary>
public interface ITenantMemberDirectoryService
{
    /// <summary>True when <paramref name="userId"/> is the owner or any kind of member of <paramref name="channelId"/>.</summary>
    Task<bool> IsMemberAsync(Guid channelId, Guid userId, CancellationToken ct = default);

    /// <summary>The tenant's people, owner first, filtered by username/display name when <paramref name="search"/> is set.</summary>
    Task<PagedList<TenantMemberDto>> ListAsync(
        Guid channelId,
        string? search,
        PaginationParams pagination,
        CancellationToken ct = default
    );
}
