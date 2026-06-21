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
