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
using NomNomzBot.Domain.Moderation.Entities;

namespace NomNomzBot.Infrastructure.Moderation.Persistence;

/// <summary>Schema J.9 — one shared-ban policy row per channel.</summary>
public class SharedBanSettingsConfiguration : IEntityTypeConfiguration<SharedBanSettings>
{
    public void Configure(EntityTypeBuilder<SharedBanSettings> builder)
    {
        builder.HasKey(e => e.Id);
        builder.HasIndex(e => e.BroadcasterId).IsUnique();
    }
}
