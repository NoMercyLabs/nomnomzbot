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
            .HasOne(e => e.Channel)
            .WithMany()
            .HasForeignKey(e => e.BroadcasterId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
