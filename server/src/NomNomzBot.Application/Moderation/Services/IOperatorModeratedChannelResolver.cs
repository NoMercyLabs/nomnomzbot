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
using NomNomzBot.Application.Contracts.Twitch;

namespace NomNomzBot.Application.Moderation.Services;

/// <summary>
/// "Every channel I moderate" for an operator (chat-client.md §3.5): the operator's own channel first, then every
/// channel Twitch's Get Moderated Channels lists for them. Twitch is the authority, never the local DB, so the set
/// can only shrink to where Twitch already made the operator a moderator.
/// </summary>
public interface IOperatorModeratedChannelResolver
{
    /// <summary>
    /// The operator's channel set. Empty when the operator owns no channel. A failure to list the moderated
    /// channels at all surfaces as a failure, never as a silent empty set.
    /// </summary>
    Task<Result<IReadOnlyList<TwitchModeratedChannel>>> ResolveAsync(
        Guid operatorUserId,
        CancellationToken ct = default
    );
}
