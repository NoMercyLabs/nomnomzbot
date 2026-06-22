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
using NomNomzBot.Domain.CustomCode.Entities;

namespace NomNomzBot.Infrastructure.Platform.Persistence.Configurations;

public class CodeScriptConfiguration : IEntityTypeConfiguration<CodeScript>
{
    public void Configure(EntityTypeBuilder<CodeScript> builder)
    {
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Name).IsRequired().HasMaxLength(100);
        builder.Property(e => e.Description).HasMaxLength(500);
        builder.Property(e => e.Language).IsRequired().HasMaxLength(20);

        builder.HasIndex(e => e.BroadcasterId);
        builder.HasIndex(e => e.CurrentVersionId);
        builder.HasIndex(e => e.IsEnabled);
        builder.HasIndex(e => e.AuthorUserId);
        // One script per (channel, name) — projection/service-enforced; soft-deletable, so a plain index.
        builder.HasIndex(e => new { e.BroadcasterId, e.Name });
    }
}
