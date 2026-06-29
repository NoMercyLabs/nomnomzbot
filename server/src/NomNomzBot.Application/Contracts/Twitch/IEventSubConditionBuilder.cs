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
/// Maps an EventSub topic to its create-time facts: the Twitch <c>condition</c> object, the topic
/// <c>version</c>, and whether the create needs the broadcaster's user token vs the app/bot token
/// (twitch-eventsub §3.3, scopes table). Keeps the per-topic knowledge in one place so the transport and the
/// service stay generic.
/// </summary>
public interface IEventSubConditionBuilder
{
    /// <summary>
    /// The condition object for <paramref name="eventType"/>. <paramref name="botTwitchUserId"/> is used for
    /// the <c>user_id</c> / <c>moderator_user_id</c> slot when a dedicated bot account is configured —
    /// critical for multi-tenant WebSocket EventSub where all subscription requests must come from the same
    /// Twitch user (the bot) regardless of which channel is being subscribed.
    /// When <paramref name="botTwitchUserId"/> is null, those slots fall back to the broadcaster id (single-user
    /// self-host where the streamer IS the bot identity).
    /// </summary>
    IReadOnlyDictionary<string, string> BuildCondition(
        string eventType,
        string twitchBroadcasterUserId,
        string? botTwitchUserId = null
    );

    /// <summary>The topic version (e.g. <c>2</c> for <c>channel.follow</c> / <c>channel.update</c>, else <c>1</c>).</summary>
    string GetVersion(string eventType);

    /// <summary>True when the create must ride the broadcaster's user token (broadcaster-scoped topic).</summary>
    bool RequiresBroadcasterToken(string eventType);
}
