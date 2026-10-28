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
