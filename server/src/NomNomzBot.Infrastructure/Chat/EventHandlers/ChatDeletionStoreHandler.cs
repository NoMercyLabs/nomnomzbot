// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.EntityFrameworkCore;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Domain.Chat.Entities;
using NomNomzBot.Domain.Chat.Events;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Infrastructure.Chat.EventHandlers;

/// <summary>
/// Mirrors Twitch chat deletions into the stored history: a chat clear, a single message delete and a per-user purge
/// each soft-delete the matching <see cref="ChatMessage"/> rows (<c>DeletedAt</c> + <c>UpdatedAt</c> = now). A row that
/// is already deleted keeps its first <c>DeletedAt</c>. Every query is scoped to the event's broadcaster.
/// </summary>
public sealed class ChatDeletionStoreHandler(IApplicationDbContext db, TimeProvider time)
    : IEventHandler<ChatClearedEvent>,
        IEventHandler<ChatMessageDeletedEvent>,
        IEventHandler<ChatUserMessagesClearedEvent>
{
    public Task HandleAsync(
        ChatClearedEvent @event,
        CancellationToken cancellationToken = default
    ) => MarkDeletedAsync(m => m.BroadcasterId == @event.BroadcasterId, cancellationToken);

    public Task HandleAsync(
        ChatMessageDeletedEvent @event,
        CancellationToken cancellationToken = default
    ) =>
        MarkDeletedAsync(
            m => m.BroadcasterId == @event.BroadcasterId && m.Id == @event.MessageId,
            cancellationToken
        );

    public Task HandleAsync(
        ChatUserMessagesClearedEvent @event,
        CancellationToken cancellationToken = default
    ) =>
        MarkDeletedAsync(
            m => m.BroadcasterId == @event.BroadcasterId && m.UserId == @event.TargetUserId,
            cancellationToken
        );

    private async Task MarkDeletedAsync(
        System.Linq.Expressions.Expression<Func<ChatMessage, bool>> scope,
        CancellationToken cancellationToken
    )
    {
        DateTime now = time.GetUtcNow().UtcDateTime;

        List<ChatMessage> rows = await db
            .ChatMessages.IgnoreQueryFilters()
            .Where(scope)
            .Where(m => m.DeletedAt == null)
            .ToListAsync(cancellationToken);

        if (rows.Count == 0)
            return;

        foreach (ChatMessage row in rows)
        {
            row.DeletedAt = now;
            row.UpdatedAt = now;
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
