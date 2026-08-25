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
