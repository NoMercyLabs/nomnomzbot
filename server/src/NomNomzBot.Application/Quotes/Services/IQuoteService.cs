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
using NomNomzBot.Application.Quotes.Dtos;

namespace NomNomzBot.Application.Quotes.Services;

/// <summary>
/// Manages a channel's numbered quote library (quotes.md §3). Numbers are per-channel monotonic
/// (allocated via <c>ITenantSequenceAllocator</c>) and never reused after deletion.
/// </summary>
public interface IQuoteService
{
    /// <summary>
    /// Allocates the next per-channel <c>Number</c> under a transaction, inserts the quote, and publishes
    /// <c>QuoteAddedEvent</c>. Returns the created quote with its assigned number.
    /// </summary>
    Task<Result<QuoteDto>> AddAsync(
        Guid broadcasterId,
        AddQuoteRequest request,
        CancellationToken ct = default
    );

    /// <summary>Gets quote <paramref name="number"/>. Fails <c>NOT_FOUND</c> when absent.</summary>
    Task<Result<QuoteDto>> GetAsync(Guid broadcasterId, int number, CancellationToken ct = default);

    /// <summary>Returns a uniformly random non-deleted quote. Fails <c>QUOTES_EMPTY</c> when the channel has none.</summary>
    Task<Result<QuoteDto>> GetRandomAsync(Guid broadcasterId, CancellationToken ct = default);

    /// <summary>Lists the channel's quotes (newest first), optionally filtered by a free-text term.</summary>
    Task<Result<PagedList<QuoteDto>>> ListAsync(
        Guid broadcasterId,
        QuoteSearch search,
        PaginationParams pagination,
        CancellationToken ct = default
    );

    /// <summary>Edits a quote's text/attribution. The <c>Number</c> is immutable.</summary>
    Task<Result<QuoteDto>> EditAsync(
        Guid broadcasterId,
        int number,
        EditQuoteRequest request,
        CancellationToken ct = default
    );

    /// <summary>Soft-deletes a quote. Its <c>Number</c> is not reused (D1).</summary>
    Task<Result> DeleteAsync(Guid broadcasterId, int number, CancellationToken ct = default);
}
