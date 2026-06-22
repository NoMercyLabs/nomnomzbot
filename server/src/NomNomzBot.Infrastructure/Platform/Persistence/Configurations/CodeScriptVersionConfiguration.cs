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
        // One row per (script, version) — append-only, not soft-deletable, so a true unique.
        builder.HasIndex(e => new { e.CodeScriptId, e.Version }).IsUnique();
    }
}
