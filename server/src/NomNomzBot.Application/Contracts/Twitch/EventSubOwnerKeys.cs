// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Application.Contracts.Twitch;

/// <summary>
/// Identifies which Twitch user's token owns a WebSocket EventSub session (twitch-eventsub §3.3). Twitch rejects
/// subscriptions created by different users on one WebSocket session ("websocket transport cannot have
/// subscriptions created by different users"), so the transport keeps ONE session per token owner: the bot's
/// session carries the chat-read topics for every channel (created with the bot token), and each broadcaster
/// gets their OWN session carrying their authorized topics (created with that broadcaster's token). The owner
/// key is the session bucket: <see cref="Bot"/> for the bot-owned set, or the broadcaster's tenant Guid string
/// for a broadcaster-owned set.
/// </summary>
public static class EventSubOwnerKeys
{
    /// <summary>The session that carries every channel's bot-owned topics (chat-read + the bot's whispers).</summary>
    public const string Bot = "bot";

    /// <summary>
    /// The owner key for a subscription: the broadcaster's tenant Guid when it rides the broadcaster's token,
    /// otherwise <see cref="Bot"/>. Mirrors <c>IEventSubConditionBuilder.RequiresBroadcasterToken</c> so the
    /// session bucket and the token that creates the sub always agree.
    /// </summary>
    public static string For(Guid broadcasterId, bool requiresBroadcasterToken) =>
        requiresBroadcasterToken ? broadcasterId.ToString() : Bot;
}
