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
