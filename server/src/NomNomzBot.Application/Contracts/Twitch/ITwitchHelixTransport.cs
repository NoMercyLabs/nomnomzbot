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

namespace NomNomzBot.Application.Contracts.Twitch;

/// <summary>
/// The DTO-agnostic Helix send pipeline — the single path every per-endpoint method rides on
/// (twitch-helix.md §"Client surface", twitch-rebuild §"Client surface"). It owns the cross-cutting
/// concerns (auth header injection, adaptive rate limiting, resilience, the <c>data[]</c> envelope, and
/// typed error mapping); the hand-written, codegen-fed sub-client methods stay thin — build a
/// <see cref="TwitchHelixRequest"/>, call one of these, map the wire DTO to the app DTO.
///
/// This is deliberately separate from the <c>ITwitchHelixClient</c> façade (twitch-helix.md §3.1):
/// the façade groups discoverable sub-clients for callers, whereas this is the low-level send seam the
/// sub-clients are built on. Naming them apart avoids a future collision when the façade lands.
/// </summary>
public interface ITwitchHelixTransport
{
    /// <summary>
    /// Sends a request expecting a single object inside the Helix <c>data[]</c> envelope and returns the
    /// first element. Empty <c>data[]</c> ⇒ <c>not_found</c>. Use for the "fetch one" endpoints
    /// (e.g. <c>GET /users?id=</c>, <c>GET /channels</c>).
    /// </summary>
    Task<Result<TResponse>> GetSingleAsync<TResponse>(
        TwitchHelixRequest request,
        CancellationToken ct = default
    );

    /// <summary>
    /// Sends a request expecting the full Helix <c>data[]</c> array and returns every element of the one
    /// response page (no auto-cursor following). Empty <c>data[]</c> ⇒ an empty list, not a failure.
    /// </summary>
    Task<Result<IReadOnlyList<TResponse>>> GetListAsync<TResponse>(
        TwitchHelixRequest request,
        CancellationToken ct = default
    );

    /// <summary>
    /// Sends a paged request and returns one page: the <c>data[]</c> items, the pagination cursor
    /// (<c>pagination.cursor</c>, null when exhausted) and the server <c>total</c>. The caller decides
    /// whether to follow the cursor.
    /// </summary>
    Task<Result<TwitchPage<TResponse>>> GetPageAsync<TResponse>(
        TwitchHelixRequest request,
        CancellationToken ct = default
    );

    /// <summary>
    /// Reads the top-level <c>total</c> field of a count-style response (e.g. <c>?first=1</c> on
    /// followers / subscriptions) without materialising the <c>data[]</c> rows.
    /// </summary>
    Task<Result<int>> GetTotalAsync(TwitchHelixRequest request, CancellationToken ct = default);

    /// <summary>
    /// Sends a request whose success body is raw non-JSON text (e.g. the schedule iCalendar's
    /// <c>text/calendar</c>) and returns it verbatim — no <c>data[]</c> envelope parsing. Non-2xx maps to
    /// the same typed errors as every other send.
    /// </summary>
    Task<Result<string>> GetRawAsync(TwitchHelixRequest request, CancellationToken ct = default);

    /// <summary>
    /// Sends a mutating request whose success is signalled only by the status code (e.g. ban / unban /
    /// add-moderator / delete-message). 2xx ⇒ success; otherwise the typed error.
    /// </summary>
    Task<Result> SendAsync(TwitchHelixRequest request, CancellationToken ct = default);

    /// <summary>
    /// Sends a mutating request that returns a body (e.g. create-poll / add-blocked-term) and returns
    /// the first <c>data[]</c> element of the response.
    /// </summary>
    Task<Result<TResponse>> SendWithResultAsync<TResponse>(
        TwitchHelixRequest request,
        CancellationToken ct = default
    );
}
