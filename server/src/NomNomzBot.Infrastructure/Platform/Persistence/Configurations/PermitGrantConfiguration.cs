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

public class PermitGrantConfiguration : IEntityTypeConfiguration<PermitGrant>
{
    public void Configure(EntityTypeBuilder<PermitGrant> builder)
    {
        builder.HasKey(e => e.Id);

        builder.Property(e => e.GrantType).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.GrantedRole).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.Reason).HasMaxLength(500);

        // Serves the per-user active-grant lookups the resolver runs and the channel grant list.
        builder.HasIndex(e => new { e.BroadcasterId, e.UserId });
    }
}
