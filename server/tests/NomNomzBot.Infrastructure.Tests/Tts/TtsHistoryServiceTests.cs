// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using FluentAssertions;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Tts.Dtos;
using NomNomzBot.Domain.Tts.Entities;
using NomNomzBot.Infrastructure.Tts;

namespace NomNomzBot.Infrastructure.Tests.Tts;

/// <summary>
/// Proves the spoken-history read: a page is newest first, shaped with the spoken text, voice, requester and
/// time; the <c>before</c> cursor walks to older rows without overlap; the page never crosses tenants; rows
/// written before the text was stored come back with a null text.
/// </summary>
public sealed class TtsHistoryServiceTests
{
    private static readonly Guid Tenant = Guid.Parse("019f2a00-4444-7000-8000-000000000001");
    private static readonly Guid OtherTenant = Guid.Parse("019f2a00-4444-7000-8000-000000000002");
    private static readonly DateTime T0 = new(2026, 10, 19, 9, 0, 0, DateTimeKind.Utc);

    private static async Task<TtsTestDbContext> SeedAsync(int count)
    {
        TtsTestDbContext db = TtsTestDbContext.New();
        for (int i = 0; i < count; i++)
            db.TtsUsageRecords.Add(Row(Tenant, $"line {i}", i));
        db.TtsUsageRecords.Add(Row(OtherTenant, "other channel", 99));
        await db.SaveChangesAsync();
        return db;
    }

    private static TtsUsageRecord Row(Guid tenant, string? text, int minute) =>
        new()
        {
            BroadcasterId = tenant,
            UserId = "viewer-1",
            CharacterCount = text?.Length ?? 0,
            Provider = "edge",
            VoiceId = "en-US-AnaNeural",
            Text = text,
            OccurredAt = T0.AddMinutes(minute),
        };

    [Fact]
    public async Task ListAsync_ReturnsNewestFirstWithTheStableShape()
    {
        await using TtsTestDbContext db = await SeedAsync(3);
        TtsHistoryService service = new(db);

        Result<TtsHistoryPageDto> result = await service.ListAsync(Tenant, 10, null);

        result.IsSuccess.Should().BeTrue();
        result.Value.Items.Select(i => i.Text).Should().Equal("line 2", "line 1", "line 0");
        TtsHistoryEntryDto first = result.Value.Items[0];
        first.VoiceId.Should().Be("en-US-AnaNeural");
        first.Provider.Should().Be("edge");
        first.RequestedByUserId.Should().Be("viewer-1");
        first.OccurredAt.Should().Be(T0.AddMinutes(2));
        result.Value.NextBefore.Should().BeNull("the page holds every row");
    }

    [Fact]
    public async Task ListAsync_BeforeCursor_WalksToOlderRowsWithoutOverlap()
    {
        await using TtsTestDbContext db = await SeedAsync(5);
        TtsHistoryService service = new(db);

        Result<TtsHistoryPageDto> page1 = await service.ListAsync(Tenant, 2, null);
        page1.Value.Items.Select(i => i.Text).Should().Equal("line 4", "line 3");
        page1.Value.NextBefore.Should().Be(page1.Value.Items[1].Id);

        Result<TtsHistoryPageDto> page2 = await service.ListAsync(
            Tenant,
            2,
            page1.Value.NextBefore
        );
        page2.Value.Items.Select(i => i.Text).Should().Equal("line 2", "line 1");

        Result<TtsHistoryPageDto> page3 = await service.ListAsync(
            Tenant,
            2,
            page2.Value.NextBefore
        );
        page3.Value.Items.Select(i => i.Text).Should().Equal("line 0");
        page3.Value.NextBefore.Should().BeNull();
    }

    [Fact]
    public async Task ListAsync_ClampsTheLimitAndKeepsRowsWithoutStoredText()
    {
        TtsTestDbContext db = TtsTestDbContext.New();
        db.TtsUsageRecords.Add(Row(Tenant, null, 0));
        await db.SaveChangesAsync();
        await using TtsTestDbContext _ = db;
        TtsHistoryService service = new(db);

        Result<TtsHistoryPageDto> result = await service.ListAsync(Tenant, 100000, null);

        result.Value.Items.Should().ContainSingle().Which.Text.Should().BeNull();
    }
}
