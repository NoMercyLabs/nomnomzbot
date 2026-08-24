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

namespace NomNomzBot.Application.Chat.Services;

/// <summary>
/// Sends a bot chat reply back on the SAME platform an inbound message arrived on (S021) — the
/// counterpart to <c>IChatProvider</c>'s tenant-keyed routing, which resolves by the channel's single
/// <c>Channel.Provider</c> field and is wrong once a channel has more than one platform connection live
/// simultaneously (D2 — e.g. Twitch AND Kick at once): a Kick message answered through
/// <c>IChatProvider</c> could go out on whichever platform happens to be the channel's primary one
/// instead of Kick. Callers that know the inbound provider — the hot chat-message command/response
/// path — MUST use this instead of <c>IChatProvider</c> for the reply/notice they send back into that
/// same conversation.
///
/// An unsupported/unregistered provider is an HONEST failure (<see cref="Result.ErrorCode"/> =
/// <c>"unsupported_provider"</c>) — it is NEVER silently routed to Twitch or any other platform, and no
/// send is attempted on any platform when the requested one isn't registered.
/// </summary>
public interface IInboundOriginChatSender
{
    /// <summary>
    /// Sends <paramref name="message"/> as the bot, on <paramref name="provider"/> specifically — not
    /// whatever platform the tenant's <c>Channel.Provider</c> happens to name.
    /// </summary>
    Task<Result> SendMessageAsync(
        Guid broadcasterId,
        string provider,
        string message,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Sends <paramref name="message"/> threaded as a reply to <paramref name="replyToMessageId"/>, on
    /// <paramref name="provider"/> specifically.
    /// </summary>
    Task<Result> SendReplyAsync(
        Guid broadcasterId,
        string provider,
        string replyToMessageId,
        string message,
        CancellationToken cancellationToken = default
    );
}
