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

namespace NomNomzBot.Infrastructure.Platform.Persistence.Configurations;

public class ErasureRequestConfiguration : IEntityTypeConfiguration<ErasureRequest>
{
    public void Configure(EntityTypeBuilder<ErasureRequest> builder)
    {
        builder.HasKey(e => e.Id);

        builder.Property(e => e.SubjectIdHash).IsRequired().HasMaxLength(64);
        builder.Property(e => e.RequestType).IsRequired().HasMaxLength(20);
        builder.Property(e => e.RequestedBy).IsRequired().HasMaxLength(20);
        builder.Property(e => e.Status).IsRequired().HasMaxLength(20);
        builder.Property(e => e.Scope).IsRequired().HasMaxLength(20);
        builder.Property(e => e.ExportLocation).HasMaxLength(2048);
        builder.Property(e => e.ExportFormat).HasMaxLength(20);

        // The my-data page reads "my requests, newest first" — index the exact access path.
        builder.HasIndex(e => new { e.SubjectUserId, e.RequestedAt });
        builder.HasIndex(e => e.Status);
    }
}
