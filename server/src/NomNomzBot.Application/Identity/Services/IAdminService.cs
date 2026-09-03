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

public interface IAdminService
{
    Task<Result<AdminStatsDto>> GetStatsAsync(CancellationToken ct = default);

    /// <summary>Paged channel listing, optionally narrowed by a case-insensitive [search] match against the
    /// channel's login/name or its owner's display name.</summary>
    Task<Result<PagedList<AdminChannelDto>>> ListChannelsAsync(
        string? search,
        PaginationParams pagination,
        CancellationToken ct = default,
        bool? isLive = null
    );

    /// <summary>Paged user listing, optionally narrowed by a case-insensitive [search] match against the
    /// user's login or display name.</summary>
    Task<Result<PagedList<AdminUserDto>>> ListUsersAsync(
        string? search,
        PaginationParams pagination,
        CancellationToken ct = default,
        string? role = null
    );

    Task<Result<AdminSystemDto>> GetSystemHealthAsync(CancellationToken ct = default);
}
