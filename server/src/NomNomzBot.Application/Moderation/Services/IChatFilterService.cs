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
using NomNomzBot.Application.Moderation.Dtos;

namespace NomNomzBot.Application.Moderation.Services;

/// <summary>
/// CRUD for a channel's custom chat filters (moderation.md J.6). The filters authored here are enforced on the
/// hot path by the chat-filter execution handler, which runs each enabled filter against every incoming message
/// and applies its action (delete / timeout / hold / flag / escalate).
/// </summary>
public interface IChatFilterService
{
    /// <summary>Lists the channel's chat filters, newest first, paged.</summary>
    Task<Result<PagedList<ChatFilterDto>>> ListAsync(
        Guid broadcasterId,
        PaginationParams pagination,
        CancellationToken ct = default
    );

    /// <summary>Fetches a single chat filter by id. <c>NOT_FOUND</c> if absent or soft-deleted.</summary>
    Task<Result<ChatFilterDto>> GetAsync(
        Guid broadcasterId,
        Guid filterId,
        CancellationToken ct = default
    );

    /// <summary>Creates a chat filter. A regex filter is rejected (<c>VALIDATION_FAILED</c>) if its pattern does not compile.</summary>
    Task<Result<ChatFilterDto>> CreateAsync(
        Guid broadcasterId,
        CreateChatFilterRequest request,
        CancellationToken ct = default
    );

    /// <summary>Patches an existing filter. <c>NOT_FOUND</c> if absent or soft-deleted.</summary>
    Task<Result<ChatFilterDto>> UpdateAsync(
        Guid broadcasterId,
        Guid filterId,
        UpdateChatFilterRequest request,
        CancellationToken ct = default
    );

    /// <summary>Soft-deletes a filter. <c>NOT_FOUND</c> if absent.</summary>
    Task<Result> DeleteAsync(Guid broadcasterId, Guid filterId, CancellationToken ct = default);
}
