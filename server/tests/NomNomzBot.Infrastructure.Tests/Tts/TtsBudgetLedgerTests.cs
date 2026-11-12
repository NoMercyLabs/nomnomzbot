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

using FluentAssertions;
using NomNomzBot.Domain.Tts.Entities;
using NomNomzBot.Infrastructure.Tts;

namespace NomNomzBot.Infrastructure.Tests.Tts;

public sealed class TtsBudgetLedgerTests
{
    private static readonly Guid Channel = Guid.Parse("019f2a00-0000-7000-8000-000000000001");

    [Fact]
    public async Task Monthly_usage_counts_only_spoken_rows_for_the_provider_and_share()
    {
        await using TtsTestDbContext db = TtsTestDbContext.New();
        DateTime month = new(2026, 11, 12, 0, 0, 0, DateTimeKind.Utc);
        db.TtsUsageRecords.AddRange(
            Row("azure", "bot", 40, month),
            Row("azure", "journal", 20, month),
            Row("azure", "bot", 9, month.AddMonths(-1)),
            Row("edge", "bot", 15, month),
            Row("edge-cached", "bot", 4, month),
            Row("azure", "bot", 100, month, failed: true)
        );
        await db.SaveChangesAsync();

        TtsBudgetUsage usage = await TtsBudgetLedger.ReadAsync(db, Channel, "azure", "bot", month);

        usage.ProviderUsed.Should().Be(60);
        usage.ShareUsed.Should().Be(40);
        (await TtsBudgetLedger.ReadAsync(db, Channel, "edge", "bot", month))
            .ProviderUsed.Should()
            .Be(19);
    }

    private static TtsUsageRecord Row(
        string provider,
        string share,
        int count,
        DateTime at,
        bool failed = false
    ) =>
        new()
        {
            BroadcasterId = Channel,
            UserId = "viewer",
            CharacterCount = count,
            Provider = provider,
            Share = share,
            VoiceId = "voice",
            OccurredAt = at,
            Failed = failed,
        };
}
