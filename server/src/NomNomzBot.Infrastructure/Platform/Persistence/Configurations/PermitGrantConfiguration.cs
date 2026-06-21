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
