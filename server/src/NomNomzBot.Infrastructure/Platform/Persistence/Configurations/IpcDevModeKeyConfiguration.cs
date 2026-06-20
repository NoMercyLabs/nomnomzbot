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
using NomNomzBot.Domain.Identity.Entities;

namespace NomNomzBot.Infrastructure.Platform.Persistence.Configurations;

public class IpcDevModeKeyConfiguration : IEntityTypeConfiguration<IpcDevModeKey>
{
    public void Configure(EntityTypeBuilder<IpcDevModeKey> builder)
    {
        builder.HasKey(e => e.Id);

        builder.Property(e => e.KeyHash).IsRequired().HasMaxLength(64);
        builder.Property(e => e.Label).HasMaxLength(100);

        builder
            .HasOne(e => e.CreatedByUser)
            .WithMany()
            .HasForeignKey(e => e.CreatedByUserId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(e => e.KeyHash).IsUnique();
    }
}
