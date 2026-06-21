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
