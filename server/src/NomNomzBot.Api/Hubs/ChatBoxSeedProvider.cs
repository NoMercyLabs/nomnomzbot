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

using System.Globalization;
using Microsoft.EntityFrameworkCore;
using NomNomzBot.Api.Hubs.Dtos;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Chat.Services;
using NomNomzBot.Application.Widgets.Dtos;
using NomNomzBot.Application.Widgets.Services;
using NomNomzBot.Domain.Chat.Entities;
using NomNomzBot.Domain.Widgets.Entities;

namespace NomNomzBot.Api.Hubs;

/// <summary>
/// Gives chat_box its conversation back on join: the newest <c>maxMessages</c> stored messages of the last
/// 30 minutes, oldest first, mapped by <see cref="ChatHistoryMapper"/> so they render exactly like live chat.
/// The window keeps a reload mid-stream full without bringing back yesterday's chat.
/// </summary>
public sealed class ChatBoxSeedProvider(
    IApplicationDbContext db,
    IChatMessageDecorator decorator,
    IHubUserEnricher enricher,
    TimeProvider clock
) : IWidgetSeedProvider
{
    private const string ChatEvent = "ChatMessage";
    private const int DefaultMaxMessages = 12; // chat_box.vue starts with maxMessages 12
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(30);

    public string NaturalKey => "chat_box";

    public async Task<IReadOnlyList<WidgetSeedFrame>> SeedAsync(
        Guid broadcasterId,
        Widget widget,
        CancellationToken cancellationToken
    )
    {
        DateTime from = (clock.GetUtcNow() - Window).UtcDateTime;
        List<ChatMessage> rows = await db
            .ChatMessages.AsNoTracking()
            .Where(m =>
                m.BroadcasterId == broadcasterId && m.DeletedAt == null && m.CreatedAt >= from
            )
            .OrderByDescending(m => m.CreatedAt)
            .Take(MaxMessages(widget))
            .ToListAsync(cancellationToken);
        rows.Reverse();

        List<DashboardChatMessageDto> messages = await ChatHistoryMapper.MapAsync(
            db,
            decorator,
            enricher,
            broadcasterId,
            broadcasterId.ToString(),
            rows,
            cancellationToken
        );
        return
        [
            .. messages.Zip(
                rows,
                (message, row) =>
                    new WidgetSeedFrame(
                        ChatEvent,
                        message,
                        new DateTimeOffset(DateTime.SpecifyKind(row.CreatedAt, DateTimeKind.Utc))
                    )
            ),
        ];
    }

    private static int MaxMessages(Widget widget) =>
        widget.Settings.TryGetValue("maxMessages", out object? raw)
        && int.TryParse(
            Convert.ToString(raw, CultureInfo.InvariantCulture),
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out int max
        )
        && max > 0
            ? max
            : DefaultMaxMessages;
}
