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
        builder.Property(e => e.PlatformSourceHash).HasMaxLength(64);

        builder.HasIndex(e => e.BroadcasterId);
        builder.HasIndex(e => e.CurrentVersionId);
        builder.HasIndex(e => e.IsEnabled);
        builder.HasIndex(e => e.AuthorUserId);
        builder.HasIndex(e => e.PlatformSourceDefinitionId);
        // One script per (channel, name) — projection/service-enforced; soft-deletable, so a plain index.
        builder.HasIndex(e => new { e.BroadcasterId, e.Name });
    }
}
