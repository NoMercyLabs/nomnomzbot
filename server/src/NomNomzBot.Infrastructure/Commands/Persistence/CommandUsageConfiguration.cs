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
using NomNomzBot.Domain.Commands.Entities;

namespace NomNomzBot.Infrastructure.Commands.Persistence;

public class CommandUsageConfiguration : IEntityTypeConfiguration<CommandUsage>
{
    public void Configure(EntityTypeBuilder<CommandUsage> builder)
    {
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedOnAdd();

        builder.Property(e => e.BroadcasterId).IsRequired();
        builder.Property(e => e.ViewerProfileId).IsRequired();
        builder.Property(e => e.ViewerUserId).IsRequired();
        builder.Property(e => e.CommandNameSnapshot).IsRequired().HasMaxLength(100);
        builder.Property(e => e.WasSuccessful).IsRequired();
        builder.Property(e => e.CreatedAt).IsRequired();

        builder
            .HasIndex(e => new { e.BroadcasterId, e.CreatedAt })
            .HasDatabaseName("IX_CommandUsage_BroadcasterId_CreatedAt");

        builder
            .HasIndex(e => new { e.CommandId, e.CreatedAt })
            .HasDatabaseName("IX_CommandUsage_CommandId_CreatedAt");
    }
}
