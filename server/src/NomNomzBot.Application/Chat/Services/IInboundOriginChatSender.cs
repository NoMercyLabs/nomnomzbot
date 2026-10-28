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
