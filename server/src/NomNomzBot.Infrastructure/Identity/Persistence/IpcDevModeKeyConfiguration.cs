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
using NomNomzBot.Domain.Identity.Entities;

namespace NomNomzBot.Infrastructure.Identity.Persistence;

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
