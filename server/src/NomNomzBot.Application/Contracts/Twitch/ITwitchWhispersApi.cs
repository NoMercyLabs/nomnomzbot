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
/// The Helix "Whispers" category sub-client: sending a whisper from the tenant to another user
/// (twitch-helix.md §3.2). One of the grouped sub-clients exposed by <see cref="ITwitchHelixClient"/>.
/// The sender is the owning tenant, passed as a <see cref="Guid"/> and resolved to its Twitch id internally
/// (the invariant: a Guid never reaches Twitch); the recipient is passed as a raw Twitch id string. Each
/// returns <see cref="Result"/> carrying a closed <see cref="TwitchErrorCodes"/> on failure.
/// </summary>
public interface ITwitchWhispersApi
{
    /// <summary>
    /// Send Whisper — sends a whisper from the tenant (<c>from_user_id</c>, resolved from the Guid) to a
    /// target user (<c>to_user_id</c>, raw Twitch id), with the text in the request body. Status-only success.
    /// Requires <c>user:manage:whispers</c>.
    /// </summary>
    Task<Result> SendWhisperAsync(
        Guid fromUserId,
        string toTwitchUserId,
        string message,
        CancellationToken ct = default
    );
}
