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
