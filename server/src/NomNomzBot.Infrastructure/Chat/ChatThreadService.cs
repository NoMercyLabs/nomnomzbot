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

using Microsoft.EntityFrameworkCore;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Chat.Services;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Domain.Chat.Entities;

namespace NomNomzBot.Infrastructure.Chat;

public sealed class ChatThreadService(IApplicationDbContext db) : IChatThreadService
{
    public async Task<Result<IReadOnlyList<ChatMessage>>> ReadAsync(
        Guid broadcasterId,
        string messageId,
        CancellationToken ct = default
    )
    {
        IQueryable<ChatMessage> channel = db
            .ChatMessages.IgnoreQueryFilters()
            .Where(row => row.BroadcasterId == broadcasterId && row.Kind == "message");
        ChatMessage? selected = await channel.FirstOrDefaultAsync(row => row.Id == messageId, ct);
        if (selected is null)
            return Result.Failure<IReadOnlyList<ChatMessage>>(
                "Chat message not found.",
                "not_found"
            );

        string rootId =
            selected.ReplyThreadParentMessageId ?? selected.ReplyToMessageId ?? selected.Id;
        // Before Twitch's thread-root field was persisted, follow each stored parent to the first message.
        if (selected.ReplyThreadParentMessageId is null)
        {
            ChatMessage cursor = selected;
            HashSet<string> seen = [selected.Id];
            while (cursor.ReplyToMessageId is { } parentId && seen.Add(parentId))
            {
                ChatMessage? parent = await channel.FirstOrDefaultAsync(
                    row => row.Id == parentId,
                    ct
                );
                if (parent is null)
                    break;

                rootId = parent.Id;
                cursor = parent;
            }
        }

        List<ChatMessage> rows = await channel
            .Where(row => row.Id == rootId || row.ReplyThreadParentMessageId == rootId)
            .ToListAsync(ct);
        if (rows.All(row => row.Id != selected.Id))
            rows.Add(selected);

        // Older rows lack a thread-root ID. Grow the thread through their direct parent links.
        HashSet<string> ids = [.. rows.Select(row => row.Id)];
        while (true)
        {
            List<ChatMessage> children = await channel
                .Where(row => row.ReplyToMessageId != null && ids.Contains(row.ReplyToMessageId))
                .ToListAsync(ct);
            List<ChatMessage> fresh = children.Where(row => ids.Add(row.Id)).ToList();
            if (fresh.Count == 0)
                break;

            rows.AddRange(fresh);
        }

        return Result.Success<IReadOnlyList<ChatMessage>>(
            rows.OrderBy(row => row.CreatedAt).ThenBy(row => row.Id).ToList()
        );
    }
}
