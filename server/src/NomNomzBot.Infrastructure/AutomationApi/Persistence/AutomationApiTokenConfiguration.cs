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
using NomNomzBot.Domain.Automation.Entities;

namespace NomNomzBot.Infrastructure.AutomationApi.Persistence;

/// <summary>Schema P.17 — external automation API tokens; secret stored as a unique SHA-256 hash only.</summary>
public class AutomationApiTokenConfiguration : IEntityTypeConfiguration<AutomationApiToken>
{
    public void Configure(EntityTypeBuilder<AutomationApiToken> builder)
    {
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Name).IsRequired().HasMaxLength(100);
        builder.Property(e => e.TokenHash).IsRequired().HasMaxLength(64);
        builder.Property(e => e.TokenPrefix).IsRequired().HasMaxLength(16);
        builder.Property(e => e.ScopesJson).IsRequired();

        builder.HasIndex(e => e.TokenHash).IsUnique();
        builder.HasIndex(e => new { e.BroadcasterId, e.Name }).IsUnique();
    }
}
