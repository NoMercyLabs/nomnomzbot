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

public class ConsentRecordConfiguration : IEntityTypeConfiguration<ConsentRecord>
{
    public void Configure(EntityTypeBuilder<ConsentRecord> builder)
    {
        builder.HasKey(e => e.Id);

        builder.Property(e => e.SubjectIdHash).IsRequired().HasMaxLength(64);
        builder.Property(e => e.ConsentType).IsRequired().HasMaxLength(50);
        builder.Property(e => e.Status).IsRequired().HasMaxLength(20);
        builder.Property(e => e.LawfulBasis).IsRequired().HasMaxLength(30);
        builder.Property(e => e.ConsentVersion).HasMaxLength(20);
        builder.Property(e => e.Source).HasMaxLength(50);
        builder.Property(e => e.IpAddressCipher).HasMaxLength(255);

        // One active row per (channel, subject, consent type) — latest-wins, enforced in the service
        // (soft-deletable, so a plain index; uniqueness is not a DB constraint here).
        builder.HasIndex(e => new
        {
            e.BroadcasterId,
            e.SubjectUserId,
            e.ConsentType,
        });
    }
}
