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

public class CodeScriptVersionConfiguration : IEntityTypeConfiguration<CodeScriptVersion>
{
    public void Configure(EntityTypeBuilder<CodeScriptVersion> builder)
    {
        builder.HasKey(e => e.Id);

        builder.Property(e => e.ValidationStatus).IsRequired().HasMaxLength(20);
        builder.Property(e => e.CompiledHash).HasMaxLength(64);

        builder.HasIndex(e => e.BroadcasterId);
        builder.HasIndex(e => e.CompiledHash);
        builder.HasIndex(e => e.ValidationStatus);
        // One row per (script, version) — append-only, version numbers are never reused even after a
        // soft delete, so this stays a true unique regardless of DeletedAt (S-OWN06).
        builder.HasIndex(e => new { e.CodeScriptId, e.Version }).IsUnique();
    }
}
