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
using Microsoft.EntityFrameworkCore;
using NomNomzBot.Application.Contracts.YouTube;
using NomNomzBot.Domain.Chat.Entities;
using NomNomzBot.Infrastructure.Chat.YouTube;

namespace NomNomzBot.Infrastructure.Tests.Chat.YouTube;

/// <summary>
/// Proves the ban-id ledger's persisted contract: a recorded ban lands with the full shape an unban needs
/// (ban id + the PRIMARY token-owner channel + type), consume returns the NEWEST live record and
/// soft-deletes it (a second consume moves on; nothing is ever hard-deleted), and tenants/viewers are
/// isolated — one channel's unban can never spend another channel's ban id.
/// </summary>
public sealed class YouTubeLiveChatBanLedgerTests
{
    private static readonly Guid Tenant = Guid.Parse("0199d000-0000-7000-8000-0000000000b1");
    private static readonly Guid OtherTenant = Guid.Parse("0199d000-0000-7000-8000-0000000000b2");
    private static readonly Guid Primary = Guid.Parse("0199d000-0000-7000-8000-0000000000b3");

    private static (YouTubeLiveChatBanLedger Ledger, BanLedgerTestDbContext Db) Build()
    {
        BanLedgerTestDbContext db = BanLedgerTestDbContext.New();
        return (new YouTubeLiveChatBanLedger(db, TimeProvider.System), db);
    }

    [Fact]
    public async Task Record_persists_the_full_shape_an_unban_needs()
    {
        (YouTubeLiveChatBanLedger ledger, BanLedgerTestDbContext db) = Build();

        await ledger.RecordAsync(Tenant, Primary, "chat-1", "UCbad", "ban-1", 600);

        YouTubeLiveChatBan row = await db.YouTubeLiveChatBans.SingleAsync();
        row.BroadcasterId.Should().Be(Tenant);
        row.PrimaryBroadcasterId.Should().Be(Primary);
        row.LiveChatId.Should().Be("chat-1");
        row.BannedChannelId.Should().Be("UCbad");
        row.BanId.Should().Be("ban-1");
        row.BanType.Should().Be("temporary");
        row.DurationSeconds.Should().Be(600);
        row.DeletedAt.Should().BeNull();
    }

    [Fact]
    public async Task Record_without_a_duration_is_a_permanent_ban()
    {
        (YouTubeLiveChatBanLedger ledger, BanLedgerTestDbContext db) = Build();

        await ledger.RecordAsync(Tenant, Primary, "chat-1", "UCworse", "ban-2", null);

        YouTubeLiveChatBan row = await db.YouTubeLiveChatBans.SingleAsync();
        row.BanType.Should().Be("permanent");
        row.DurationSeconds.Should().BeNull();
    }

    [Fact]
    public async Task Consume_returns_the_newest_record_and_soft_deletes_it()
    {
        (YouTubeLiveChatBanLedger ledger, BanLedgerTestDbContext db) = Build();
        // A timeout later escalated to a permanent ban: the unban must lift the NEWEST (the permanent one).
        await ledger.RecordAsync(Tenant, Primary, "chat-1", "UCbad", "ban-old", 600);
        await ledger.RecordAsync(Tenant, Primary, "chat-1", "UCbad", "ban-new", null);

        YouTubeConsumedBan? first = await ledger.ConsumeLatestAsync(Tenant, "UCbad");
        first.Should().NotBeNull();
        first!.BanId.Should().Be("ban-new");
        first.PrimaryBroadcasterId.Should().Be(Primary);

        // The consumed row is soft-deleted — auditable, never hard-deleted — and the next consume moves on
        // to the older record instead of re-spending the same id.
        (await db.YouTubeLiveChatBans.CountAsync(b => b.DeletedAt != null))
            .Should()
            .Be(1);
        YouTubeConsumedBan? second = await ledger.ConsumeLatestAsync(Tenant, "UCbad");
        second!.BanId.Should().Be("ban-old");

        (await ledger.ConsumeLatestAsync(Tenant, "UCbad")).Should().BeNull();
        (await db.YouTubeLiveChatBans.CountAsync()).Should().Be(2, "consume never hard-deletes");
    }

    [Fact]
    public async Task Consume_is_isolated_per_tenant_and_per_viewer()
    {
        (YouTubeLiveChatBanLedger ledger, _) = Build();
        await ledger.RecordAsync(Tenant, Primary, "chat-1", "UCbad", "ban-mine", null);

        // Another tenant, and another viewer of the same tenant, must find nothing.
        (await ledger.ConsumeLatestAsync(OtherTenant, "UCbad"))
            .Should()
            .BeNull();
        (await ledger.ConsumeLatestAsync(Tenant, "UCother")).Should().BeNull();

        // The real record is still live for its owner.
        (await ledger.ConsumeLatestAsync(Tenant, "UCbad"))!
            .BanId.Should()
            .Be("ban-mine");
    }
}
