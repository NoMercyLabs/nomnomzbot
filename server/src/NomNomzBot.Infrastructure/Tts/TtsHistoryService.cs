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
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Tts.Dtos;
using NomNomzBot.Application.Tts.Services;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Tts.Entities;

namespace NomNomzBot.Infrastructure.Tts;

/// <summary>
/// The spoken history over <see cref="TtsUsageRecord"/>: newest first, keyed by the row id, so a
/// <c>before</c> cursor walks to older rows with no overlap even when two rows share a timestamp.
/// </summary>
public class TtsHistoryService(IApplicationDbContext db) : ITtsHistoryService
{
    private const int MaxLimit = 100;

    public async Task<Result<TtsHistoryPageDto>> ListAsync(
        Guid broadcasterId,
        int limit,
        int? before,
        CancellationToken cancellationToken = default
    )
    {
        int take = Math.Clamp(limit, 1, MaxLimit);

        IQueryable<TtsUsageRecord> query = db.TtsUsageRecords.Where(r =>
            r.BroadcasterId == broadcasterId && !r.Failed
        );
        if (before is not null)
            query = query.Where(r => r.Id < before);

        // One extra row tells whether an older page exists.
        List<TtsUsageRecord> rows = await query
            .OrderByDescending(r => r.Id)
            .Take(take + 1)
            .ToListAsync(cancellationToken);

        bool hasMore = rows.Count > take;
        List<string> requesterIds = rows.Take(take).Select(r => r.UserId).Distinct().ToList();
        Dictionary<string, User> requesters = (
            await db
                .Users.Where(u => u.TwitchUserId != null && requesterIds.Contains(u.TwitchUserId))
                .ToListAsync(cancellationToken)
        ).ToDictionary(u => u.TwitchUserId!);

        List<TtsHistoryEntryDto> items =
        [
            .. rows.Take(take)
                .Select(r =>
                {
                    requesters.TryGetValue(r.UserId, out User? requester);
                    return new TtsHistoryEntryDto(
                        r.Id,
                        r.Text,
                        r.VoiceId,
                        r.Provider,
                        r.UserId,
                        r.OccurredAt,
                        requester?.DisplayName,
                        requester?.Color
                    );
                }),
        ];

        return Result.Success(new TtsHistoryPageDto(items, hasMore ? items[^1].Id : null));
    }
}
