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

namespace NomNomzBot.Application.Moderation.Services;

/// <summary>
/// Deletes a single chat message AS THE LOGGED-IN OPERATOR (chat-client.md §3.5) — the dashboard chat page's
/// delete quick-action, the moderation counterpart to <c>IOperatorChatSender</c>. It resolves the target channel's
/// Twitch id from the tenant Guid and issues the Helix Delete Chat Message on the <em>operator's own</em> token with
/// the operator's Twitch user id as <c>moderator_id</c>, so Twitch attributes the removal to the moderator who
/// clicked — not the broadcaster. Twitch is the authority on whether the operator may moderate there (a missing
/// scope or a not-actually-a-mod surfaces as a typed failure, never a silent success).
/// </summary>
public interface IOperatorMessageDeleter
{
    /// <summary>
    /// Deletes <paramref name="messageId"/> from <paramref name="broadcasterId"/> as <paramref name="operatorUserId"/>.
    /// Returns a failure (never throws) with <c>not_found</c> when the channel is unknown locally, <c>no_token</c>
    /// when the operator has no linked Twitch identity to act as, or the mapped Twitch error when Twitch rejects it.
    /// </summary>
    Task<Result> DeleteAsUserAsync(
        Guid operatorUserId,
        Guid broadcasterId,
        string messageId,
        CancellationToken ct = default
    );
}
