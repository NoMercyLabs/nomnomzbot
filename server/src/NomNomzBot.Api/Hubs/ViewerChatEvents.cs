// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Api.Hubs.Dtos;

namespace NomNomzBot.Api.Hubs;

/// <summary>
/// The viewer chat group (chat-client.md §9a): which channel events it mirrors, and the viewer-safe shape of each.
/// A viewer learns that a line was removed, never which moderator removed it.
/// </summary>
public static class ViewerChatEvents
{
    public static string Group(string broadcasterId) => $"viewer-chat:{broadcasterId}";

    public static bool IsMirrored(string method) =>
        method is "message_deleted" or "chat_cleared" or "user_messages_cleared";

    public static object ViewerSafe(object data) =>
        data switch
        {
            MessageDeletedDto deleted => deleted with
            {
                DeletedByUserId = string.Empty,
                DeletedByDisplayName = string.Empty,
            },
            ChatClearedDto => new ChatClearedDto(string.Empty),
            _ => data,
        };
}
