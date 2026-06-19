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

    Task<Result<PagedList<AdminChannelDto>>> ListChannelsAsync(
        PaginationParams pagination,
        CancellationToken ct = default
    );

    Task<Result<PagedList<AdminUserDto>>> ListUsersAsync(
        PaginationParams pagination,
        CancellationToken ct = default
    );

    Task<Result<AdminSystemDto>> GetSystemHealthAsync(CancellationToken ct = default);
}
