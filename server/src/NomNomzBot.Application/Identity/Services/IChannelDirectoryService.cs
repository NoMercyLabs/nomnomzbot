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
/// The viewer chat directory (chat-client.md §9a "Any Twitch channel"): the bot channels first, then on the
/// first page of a non-empty search the Twitch channels this deployment does not serve.
/// </summary>
public interface IChannelDirectoryService
{
    Task<Result<PagedList<PublicChannelDto>>> SearchAsync(
        string? query,
        PaginationParams pagination,
        CancellationToken cancellationToken = default
    );
}
