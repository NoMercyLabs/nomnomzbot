// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Dashboard.Stats;
using NomNomzBot.Domain.Chat.Entities;

namespace NomNomzBot.Infrastructure.Dashboard.Stats;

public static class StreamChatWindow
{
    /// <summary>
    /// The chat of one stream. The live writer never sets <c>ChatMessage.StreamId</c> (0 of 58685 rows on the dev
    /// box), so a message belongs to a stream by its time, like every other recap metric.
    /// </summary>
    public static IQueryable<ChatMessage> InWindow(
        this IQueryable<ChatMessage> messages,
        StreamWindow window
    )
    {
        DateTime from = window.From;
        DateTime to = window.To;
        return messages.Where(m =>
            m.BroadcasterId == window.ChannelId && m.CreatedAt >= from && m.CreatedAt <= to
        );
    }
}
