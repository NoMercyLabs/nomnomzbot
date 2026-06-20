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
/// The Helix "Moderators &amp; VIPs" category sub-client: the channel's moderator and VIP rosters plus the
/// channels a user moderates (twitch-helix.md §3.2). One of the grouped sub-clients exposed by
/// <see cref="ITwitchHelixClient"/>. Every method takes the owning tenant as a <see cref="Guid"/> and
/// resolves it to the Twitch id internally (the invariant: a Guid never reaches Twitch); the target user
/// of a mutation is its raw Twitch id. Each returns <see cref="Result"/>/<see cref="Result{T}"/> carrying
/// a closed <see cref="TwitchErrorCodes"/> on failure.
/// </summary>
public interface ITwitchModeratorsApi
{
    /// <summary>Get Moderators — one page of the channel's moderators. Requires <c>moderation:read</c>.</summary>
    Task<Result<TwitchPage<TwitchModerator>>> GetModeratorsAsync(
        Guid broadcasterId,
        TwitchPageRequest page,
        CancellationToken ct = default
    );

    /// <summary>Add Channel Moderator — grants the target user moderator privileges. Requires <c>channel:manage:moderators</c>.</summary>
    Task<Result> AddModeratorAsync(
        Guid broadcasterId,
        string targetTwitchUserId,
        CancellationToken ct = default
    );

    /// <summary>Remove Channel Moderator — revokes the target user's moderator privileges. Requires <c>channel:manage:moderators</c>.</summary>
    Task<Result> RemoveModeratorAsync(
        Guid broadcasterId,
        string targetTwitchUserId,
        CancellationToken ct = default
    );

    /// <summary>Get VIPs — one page of the channel's VIPs. Requires <c>channel:read:vips</c>.</summary>
    Task<Result<TwitchPage<TwitchVip>>> GetVipsAsync(
        Guid broadcasterId,
        TwitchPageRequest page,
        CancellationToken ct = default
    );

    /// <summary>Add Channel VIP — grants the target user VIP status. Requires <c>channel:manage:vips</c>.</summary>
    Task<Result> AddVipAsync(
        Guid broadcasterId,
        string targetTwitchUserId,
        CancellationToken ct = default
    );

    /// <summary>Remove Channel VIP — revokes the target user's VIP status. Requires <c>channel:manage:vips</c>.</summary>
    Task<Result> RemoveVipAsync(
        Guid broadcasterId,
        string targetTwitchUserId,
        CancellationToken ct = default
    );

    /// <summary>
    /// Get Moderated Channels — one page of channels the user has moderator privileges in. The user is
    /// resolved to its Twitch user id internally. Requires <c>user:read:moderated_channels</c>.
    /// </summary>
    Task<Result<TwitchPage<TwitchModeratedChannel>>> GetModeratedChannelsAsync(
        Guid userId,
        TwitchPageRequest page,
        CancellationToken ct = default
    );
}
