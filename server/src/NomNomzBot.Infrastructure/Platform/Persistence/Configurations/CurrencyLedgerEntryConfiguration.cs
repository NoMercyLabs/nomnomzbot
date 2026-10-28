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
