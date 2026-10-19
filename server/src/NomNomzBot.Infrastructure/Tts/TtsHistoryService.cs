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
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Tts.Dtos;
using NomNomzBot.Application.Tts.Services;
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
            r.BroadcasterId == broadcasterId
        );
        if (before is not null)
            query = query.Where(r => r.Id < before);

        // One extra row tells whether an older page exists.
        List<TtsUsageRecord> rows = await query
            .OrderByDescending(r => r.Id)
            .Take(take + 1)
            .ToListAsync(cancellationToken);

        bool hasMore = rows.Count > take;
        List<TtsHistoryEntryDto> items =
        [
            .. rows.Take(take)
                .Select(r => new TtsHistoryEntryDto(
                    r.Id,
                    r.Text,
                    r.VoiceId,
                    r.Provider,
                    r.UserId,
                    r.OccurredAt
                )),
        ];

        return Result.Success(new TtsHistoryPageDto(items, hasMore ? items[^1].Id : null));
    }
}
