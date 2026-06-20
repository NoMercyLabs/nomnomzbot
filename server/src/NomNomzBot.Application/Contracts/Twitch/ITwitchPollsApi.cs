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
/// The Helix "Polls" category sub-client: list, create and end the broadcaster's polls
/// (twitch-helix.md §3.2). One of the grouped sub-clients exposed by <see cref="ITwitchHelixClient"/>.
/// Every method takes the owning tenant as a <see cref="Guid"/> and resolves it to the Twitch id internally
/// (the invariant: a Guid never reaches Twitch) — including the body-borne <c>broadcaster_id</c> that the
/// create/end endpoints expect. Each returns <see cref="Result"/>/<see cref="Result{T}"/> carrying a closed
/// <see cref="TwitchErrorCodes"/> on failure. All calls use the broadcaster's user token.
/// </summary>
public interface ITwitchPollsApi
{
    /// <summary>
    /// Get Polls — one page of the broadcaster's polls, newest first. Optionally filtered to specific poll
    /// ids (raw Twitch ids). Requires <c>channel:read:polls</c>.
    /// </summary>
    Task<Result<TwitchPage<TwitchPoll>>> GetPollsAsync(
        Guid broadcasterId,
        IReadOnlyList<string>? pollIds,
        TwitchPageRequest page,
        CancellationToken ct = default
    );

    /// <summary>
    /// Create Poll — starts a poll viewers can vote on; the resolved channel id is placed in the request
    /// body as Twitch requires. Returns the created poll. Requires <c>channel:manage:polls</c>.
    /// </summary>
    Task<Result<TwitchPoll>> CreatePollAsync(
        Guid broadcasterId,
        CreatePollRequest request,
        CancellationToken ct = default
    );

    /// <summary>
    /// End Poll — ends an active poll, either <c>TERMINATED</c> (end, stay visible) or <c>ARCHIVED</c>
    /// (end and hide); the resolved channel id is placed in the request body as Twitch requires. Returns
    /// the ended poll. Requires <c>channel:manage:polls</c>.
    /// </summary>
    Task<Result<TwitchPoll>> EndPollAsync(
        Guid broadcasterId,
        string pollId,
        string status,
        CancellationToken ct = default
    );
}
