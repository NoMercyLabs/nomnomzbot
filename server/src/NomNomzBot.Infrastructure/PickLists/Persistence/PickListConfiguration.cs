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
using NomNomzBot.Domain.PickLists.Entities;

namespace NomNomzBot.Infrastructure.PickLists.Persistence;

public class PickListConfiguration : IEntityTypeConfiguration<PickList>
{
    public void Configure(EntityTypeBuilder<PickList> builder)
    {
        builder.HasKey(e => e.Id);

        builder.Property(e => e.BroadcasterId).IsRequired();

        builder.Property(e => e.Name).IsRequired().HasMaxLength(100);

        builder.Property(e => e.Description).HasMaxLength(500);

        // Items needs no explicit converter: Npgsql maps List<string> to text[], and the SQLite compatibility
        // shim (ProviderCompatibilityExtensions) rewrites it to a JSON TEXT column — mirrors Timer.Messages.

        // One live list per (channel, name). Filtered on DeletedAt IS NULL (the codebase's partial-unique-index
        // convention) so a soft-deleted row keeps its name free to be revived, never colliding with a new one.
        builder
            .HasIndex(e => new { e.BroadcasterId, e.Name })
            .IsUnique()
            .HasDatabaseName("IX_PickList_BroadcasterId_Name")
            .HasFilter("\"DeletedAt\" IS NULL");

        builder
            .HasIndex(e => e.PlatformSourceDefinitionId)
            .HasDatabaseName("IX_PickList_PlatformSourceDefinitionId");

        builder
            .HasOne(e => e.Channel)
            .WithMany()
            .HasForeignKey(e => e.BroadcasterId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
