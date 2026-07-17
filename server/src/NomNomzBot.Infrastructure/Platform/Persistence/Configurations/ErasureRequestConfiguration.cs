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
