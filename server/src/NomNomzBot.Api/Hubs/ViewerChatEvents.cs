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
