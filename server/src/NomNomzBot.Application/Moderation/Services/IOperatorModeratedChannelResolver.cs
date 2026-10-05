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
