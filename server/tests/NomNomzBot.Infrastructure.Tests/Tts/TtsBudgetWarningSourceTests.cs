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
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Domain.Tts.Entities;
using NomNomzBot.Infrastructure.Notifications.Sources;

namespace NomNomzBot.Infrastructure.Tests.Tts;

public sealed class TtsBudgetWarningSourceTests
{
    [Fact]
    public async Task Owner_inbox_shows_current_threshold_and_respects_its_dismissal()
    {
        Guid channel = Guid.Parse("019f2a00-0000-7000-8000-000000000001");
        DateTimeOffset now = new(2026, 11, 12, 0, 0, 0, TimeSpan.Zero);
        FakeTimeProvider clock = new(now);
        await using TtsTestDbContext db = TtsTestDbContext.New();
        db.TtsConfigs.Add(
            new TtsConfig
            {
                BroadcasterId = channel,
                ProviderBudgetsJson =
                    "[{\"Provider\":\"azure\",\"MonthlyCharacters\":100,\"Shares\":[]}]",
            }
        );
        db.TtsUsageRecords.Add(
            new TtsUsageRecord
            {
                BroadcasterId = channel,
                UserId = "viewer",
                Provider = "azure",
                Share = "default",
                CharacterCount = 80,
                VoiceId = "voice",
                OccurredAt = now.UtcDateTime,
            }
        );
        await db.SaveChangesAsync();
        TtsBudgetWarningSource source = new(db, clock);

        List<Application.Notifications.Dtos.ActionRequiredItemDto> first = (
            await source.GetItemsAsync(channel, new HashSet<string>())
        ).Value;
        first.Should().ContainSingle();
        first[0].Parameters["threshold"].Should().Be("80");
        first[0].DeepLinkRoute.Should().Be("tts?view=keys");
        (await source.GetItemsAsync(channel, new HashSet<string> { first[0].Id }))
            .Value.Should()
            .BeEmpty();

        db.TtsUsageRecords.Add(
            new TtsUsageRecord
            {
                BroadcasterId = channel,
                UserId = "viewer",
                Provider = "azure",
                Share = "default",
                CharacterCount = 20,
                VoiceId = "voice",
                OccurredAt = now.UtcDateTime,
            }
        );
        await db.SaveChangesAsync();

        List<Application.Notifications.Dtos.ActionRequiredItemDto> full = (
            await source.GetItemsAsync(channel, new HashSet<string> { first[0].Id })
        ).Value;
        full.Should().ContainSingle();
        full[0].Parameters["threshold"].Should().Be("100");
    }
}
