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

public class SavingsJarMembershipConfiguration : IEntityTypeConfiguration<SavingsJarMembership>
{
    public void Configure(EntityTypeBuilder<SavingsJarMembership> builder)
    {
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Role).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.Status).HasConversion<string>().HasMaxLength(20);

        // One membership per (jar, channel) — enforced in the service. Cross-tenant: no tenant filter.
        builder.HasIndex(e => new { e.JarId, e.MemberBroadcasterId });
        builder.HasIndex(e => e.MemberBroadcasterId);
    }
}
