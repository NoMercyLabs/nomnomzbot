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
using NomNomzBot.Domain.Tts.Entities;

namespace NomNomzBot.Infrastructure.Tts;

public sealed record TtsBudgetUsage(int ProviderUsed, int ShareUsed);

public static class TtsBudgetLedger
{
    public static async Task<TtsBudgetUsage> ReadAsync(
        IApplicationDbContext db,
        Guid channelId,
        string provider,
        string share,
        DateTime at,
        CancellationToken ct = default
    )
    {
        DateTime start = new(at.Year, at.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        DateTime end = start.AddMonths(1);
        List<TtsUsageRecord> rows = await db
            .TtsUsageRecords.Where(row =>
                row.BroadcasterId == channelId
                && (
                    row.Provider == provider
                    || (provider == "edge" && row.Provider == "edge-cached")
                )
                && !row.Failed
                && row.OccurredAt >= start
                && row.OccurredAt < end
            )
            .ToListAsync(ct);
        return new(
            rows.Sum(row => row.CharacterCount),
            rows.Where(row => row.Share == share).Sum(row => row.CharacterCount)
        );
    }
}
