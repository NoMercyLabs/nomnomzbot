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

public class SecurityNoticeConfiguration : IEntityTypeConfiguration<SecurityNotice>
{
    public void Configure(EntityTypeBuilder<SecurityNotice> builder)
    {
        builder.HasKey(e => e.Id);

        builder.Property(e => e.BroadcasterId).IsRequired();

        builder.Property(e => e.NoticeType).IsRequired().HasMaxLength(64);

        builder.Property(e => e.Summary).IsRequired().HasMaxLength(500);

        builder.Property(e => e.Reason).HasMaxLength(1000);

        builder.Property(e => e.Scope).HasMaxLength(200);

        // Newest-first listing per channel is the only read pattern the owner-facing list uses.
        builder.HasIndex(e => new { e.BroadcasterId, e.CreatedAt });
    }
}
