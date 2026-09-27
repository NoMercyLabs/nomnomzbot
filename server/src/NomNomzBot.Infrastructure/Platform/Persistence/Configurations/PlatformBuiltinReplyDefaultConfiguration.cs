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
using NomNomzBot.Domain.Commands.Entities;

namespace NomNomzBot.Infrastructure.Platform.Persistence.Configurations;

public class PlatformBuiltinReplyDefaultConfiguration
    : IEntityTypeConfiguration<PlatformBuiltinReplyDefault>
{
    public void Configure(EntityTypeBuilder<PlatformBuiltinReplyDefault> builder)
    {
        builder.HasKey(e => e.Id);
        builder.Property(e => e.BuiltinKey).IsRequired().HasMaxLength(50);
        builder.Property(e => e.Slot).IsRequired().HasMaxLength(50);
        builder.Property(e => e.Template).IsRequired().HasMaxLength(500);

        // One admin text per built-in response slot.
        builder.HasIndex(e => new { e.BuiltinKey, e.Slot }).IsUnique();
    }
}
