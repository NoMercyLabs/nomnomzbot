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

public class BotAccountConfiguration : IEntityTypeConfiguration<BotAccount>
{
    public void Configure(EntityTypeBuilder<BotAccount> builder)
    {
        builder.HasKey(e => e.Id);

        builder.Property(e => e.IdentityType).IsRequired().HasMaxLength(10);
        builder.Property(e => e.Platform).IsRequired().HasMaxLength(20);
        builder.Property(e => e.BotUserId).IsRequired().HasMaxLength(50);
        builder.Property(e => e.BotUsername).IsRequired().HasMaxLength(255);

        builder.HasIndex(e => e.BotUserId).IsUnique();
        builder.HasIndex(e => new { e.Platform, e.IdentityType });
    }
}
