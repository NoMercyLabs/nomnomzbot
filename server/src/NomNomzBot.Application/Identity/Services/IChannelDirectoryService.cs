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
