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
/// The Helix "Users" category sub-client: user lookups, the tenant's own profile description, and the
/// block list (twitch-helix.md §3.2). One of the grouped sub-clients exposed by <see cref="ITwitchHelixClient"/>.
/// Every method takes the owning tenant as a <see cref="Guid"/> and resolves it to the Twitch id internally
/// (the invariant: a Guid never reaches Twitch); other users are passed as raw Twitch id strings. Each
/// returns <see cref="Result"/>/<see cref="Result{T}"/> carrying a closed <see cref="TwitchErrorCodes"/> on failure.
/// </summary>
public interface ITwitchUsersApi
{
    /// <summary>Get Users — look up user records by Twitch id (multiple <c>id=</c> params). App token; no scope. Email is never requested.</summary>
    Task<Result<IReadOnlyList<TwitchUser>>> GetUsersByIdsAsync(
        IReadOnlyList<string> twitchUserIds,
        CancellationToken ct = default
    );

    /// <summary>Get Users — look up user records by login name (multiple <c>login=</c> params). App token; no scope. Email is never requested.</summary>
    Task<Result<IReadOnlyList<TwitchUser>>> GetUsersByLoginsAsync(
        IReadOnlyList<string> logins,
        CancellationToken ct = default
    );

    /// <summary>Update User — set the tenant's own profile description (<c>description</c> query param). Requires <c>user:edit</c>. Returns the updated user.</summary>
    Task<Result<TwitchUser>> UpdateDescriptionAsync(
        Guid broadcasterId,
        string description,
        CancellationToken ct = default
    );

    /// <summary>Get User Block List — one page of the tenant's blocked users. Requires <c>user:read:blocked_users</c>.</summary>
    Task<Result<TwitchPage<TwitchBlockedUser>>> GetBlockListAsync(
        Guid broadcasterId,
        TwitchPageRequest page,
        CancellationToken ct = default
    );

    /// <summary>
    /// Block User — blocks a target user (raw Twitch id) from contacting the tenant. Optional
    /// <c>source_context</c> (<c>chat</c>/<c>whisper</c>) and <c>reason</c> (<c>harassment</c>/<c>spam</c>/<c>other</c>).
    /// Status-only success. Requires <c>user:manage:blocked_users</c>.
    /// </summary>
    Task<Result> BlockUserAsync(
        Guid broadcasterId,
        string targetTwitchUserId,
        string? sourceContext = null,
        string? reason = null,
        CancellationToken ct = default
    );

    /// <summary>Unblock User — removes a target user (raw Twitch id) from the tenant's block list. Status-only. Requires <c>user:manage:blocked_users</c>.</summary>
    Task<Result> UnblockUserAsync(
        Guid broadcasterId,
        string targetTwitchUserId,
        CancellationToken ct = default
    );
}
