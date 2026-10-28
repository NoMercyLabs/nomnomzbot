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

    /// <summary>
    /// Send Whisper as the platform bot: <c>from_user_id</c> is the bot account's Twitch id (the recipient of the
    /// whisper being answered) and the call rides the bot's own user token. Requires <c>user:manage:whispers</c>
    /// on the bot; without it the call short-circuits with <c>missing_scope</c> and raises the missing-scope card
    /// on <paramref name="channelId"/> (<see cref="Guid.Empty"/> when no channel is known: no card).
    /// </summary>
    Task<Result> SendAsBotAsync(
        Guid channelId,
        string botTwitchUserId,
        string toTwitchUserId,
        string message,
        CancellationToken ct = default
    );
}
