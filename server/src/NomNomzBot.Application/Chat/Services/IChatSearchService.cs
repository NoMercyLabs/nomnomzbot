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
using NomNomzBot.Domain.Chat.Entities;

namespace NomNomzBot.Application.Chat.Services;

/// <summary>
/// One page of a chat-history search: the matching rows, newest first, and the opaque cursor to resume the scan
/// further back (null when the scan reached the channel's first stored message).
/// </summary>
public sealed record ChatSearchPage(IReadOnlyList<ChatMessage> Messages, string? NextCursor);

/// <summary>
/// Fuzzy search over a channel's whole stored chat history, newest to oldest. The scan is bounded per request, so
/// a page can come back short with a cursor; the caller keeps paging until the cursor is null.
/// </summary>
public interface IChatSearchService
{
    /// <summary>The shortest query the search accepts.</summary>
    public const int MinQueryLength = 2;

    /// <summary>
    /// Finds up to <paramref name="limit"/> messages that fuzzily match <paramref name="query"/>. Fails with
    /// <c>invalid_query</c> for a query shorter than <see cref="MinQueryLength"/> and <c>invalid_cursor</c> for a
    /// <paramref name="before"/> cursor this service did not issue.
    /// </summary>
    Task<Result<ChatSearchPage>> SearchAsync(
        Guid broadcasterId,
        string query,
        int limit,
        string? before,
        CancellationToken ct = default
    );
}
