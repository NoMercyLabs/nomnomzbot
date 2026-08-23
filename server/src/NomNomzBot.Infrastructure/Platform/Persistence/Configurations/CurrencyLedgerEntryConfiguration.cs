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
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NomNomzBot.Domain.Economy.Entities;

namespace NomNomzBot.Infrastructure.Platform.Persistence.Configurations;

public class CurrencyLedgerEntryConfiguration : IEntityTypeConfiguration<CurrencyLedgerEntry>
{
    public void Configure(EntityTypeBuilder<CurrencyLedgerEntry> builder)
    {
        builder.HasKey(e => e.Id); // long identity

        builder.Property(e => e.ViewerTwitchUserId).IsRequired().HasMaxLength(50);
        builder.Property(e => e.EntryType).HasConversion<string>().HasMaxLength(30);
        builder.Property(e => e.SourceType).HasConversion<string>().HasMaxLength(30);
        builder.Property(e => e.Reason).HasMaxLength(500);

        // Append-only: the per-tenant position is unique + gap-free (one entry per allocated position).
        builder.HasIndex(e => new { e.BroadcasterId, e.TenantPosition }).IsUnique();
        // S005/F12: refuse a duplicate earning credit at the DB level — a redelivered/retried
        // EventId for the same viewer + entry type can only ever post once. NULL EventId entries
        // (manual adjustments, transfers, purchases) are not constrained by this index.
        builder
            .HasIndex(e => new
            {
                e.BroadcasterId,
                e.ViewerUserId,
                e.EventId,
                e.EntryType,
            })
            .IsUnique()
            .HasDatabaseName("IX_CurrencyLedgerEntries_Broadcaster_Viewer_EventId_EntryType")
            .HasFilter("\"EventId\" IS NOT NULL");
        // Serves the per-account balance fold + ledger history (newest-first by Id).
        builder.HasIndex(e => new
        {
            e.BroadcasterId,
            e.AccountId,
            e.Id,
        });
    }
}
