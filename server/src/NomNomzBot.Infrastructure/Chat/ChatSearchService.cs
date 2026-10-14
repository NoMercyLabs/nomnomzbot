// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using FuzzySharp;
using Microsoft.EntityFrameworkCore;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Chat.Services;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Domain.Chat.Entities;

namespace NomNomzBot.Infrastructure.Chat;

/// <summary>
/// Scans a channel's chat history newest to oldest by keyset <c>(CreatedAt, Id)</c> in batches, scoring only the
/// message text, and loads the full rows of the matches. Bounded per request so a huge channel cannot pin the
/// database; the returned cursor lets the caller continue.
/// </summary>
public sealed class ChatSearchService(IApplicationDbContext db) : IChatSearchService
{
    private const int BatchSize = 2000;
    private const int MaxScannedRows = 50_000;
    private const int MatchThreshold = 80;

    public async Task<Result<ChatSearchPage>> SearchAsync(
        Guid broadcasterId,
        string query,
        int limit,
        string? before,
        CancellationToken ct = default
    )
    {
        string needle = query.Trim().ToLowerInvariant();
        if (needle.Length < IChatSearchService.MinQueryLength)
            return Result.Failure<ChatSearchPage>(
                $"Search text needs at least {IChatSearchService.MinQueryLength} characters.",
                "invalid_query"
            );

        ChatSearchCursor? position = null;
        if (before is not null)
        {
            if (!ChatSearchCursor.TryDecode(before, out ChatSearchCursor decoded))
                return Result.Failure<ChatSearchPage>("Invalid search cursor.", "invalid_cursor");

            position = decoded;
        }

        List<string> matchedIds = [];
        int scanned = 0;
        bool reachedStart = false;

        while (matchedIds.Count < limit && scanned < MaxScannedRows)
        {
            int take = Math.Min(BatchSize, MaxScannedRows - scanned);
            var batch = await BatchQuery(broadcasterId, position)
                .Select(m => new
                {
                    m.Id,
                    m.CreatedAt,
                    m.Message,
                })
                .Take(take)
                .ToListAsync(ct);

            foreach (var row in batch)
            {
                scanned++;
                position = new(row.CreatedAt, row.Id);

                if (Fuzz.PartialRatio(needle, row.Message.ToLowerInvariant()) >= MatchThreshold)
                    matchedIds.Add(row.Id);

                if (matchedIds.Count >= limit)
                    break;
            }

            // A full page ends the scan mid-batch: the rows after the last match stay unscanned for the cursor.
            if (matchedIds.Count >= limit)
                break;

            if (batch.Count < take)
            {
                reachedStart = true;
                break;
            }
        }

        List<ChatMessage> messages = await LoadMatches(broadcasterId, matchedIds, ct);
        string? nextCursor = reachedStart || position is null ? null : position.Value.Encode();

        return Result.Success(new ChatSearchPage(messages, nextCursor));
    }

    // Live rows of the channel, newest first, strictly older than the cursor position when there is one.
    private IQueryable<ChatMessage> BatchQuery(Guid broadcasterId, ChatSearchCursor? position)
    {
        IQueryable<ChatMessage> rows = db.ChatMessages.Where(m =>
            m.BroadcasterId == broadcasterId && m.DeletedAt == null
        );

        if (position is { } at)
            rows = rows.Where(m =>
                m.CreatedAt < at.CreatedAt
                || (m.CreatedAt == at.CreatedAt && m.Id.CompareTo(at.Id) < 0)
            );

        return rows.OrderByDescending(m => m.CreatedAt).ThenByDescending(m => m.Id);
    }

    private async Task<List<ChatMessage>> LoadMatches(
        Guid broadcasterId,
        List<string> ids,
        CancellationToken ct
    ) =>
        ids.Count == 0
            ? []
            : await db
                .ChatMessages.Where(m => m.BroadcasterId == broadcasterId && ids.Contains(m.Id))
                .OrderByDescending(m => m.CreatedAt)
                .ThenByDescending(m => m.Id)
                .ToListAsync(ct);
}
