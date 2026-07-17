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
using NomNomzBot.Domain.Obs.Entities;

namespace NomNomzBot.Infrastructure.Obs.Persistence;

/// <summary>Schema P.14 — one OBS connection per channel; the bridge token is unique platform-wide.</summary>
public class ObsConnectionConfiguration : IEntityTypeConfiguration<ObsConnection>
{
    public void Configure(EntityTypeBuilder<ObsConnection> builder)
    {
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Mode).IsRequired().HasMaxLength(10);

        builder.HasIndex(e => e.BroadcasterId).IsUnique();
        builder.HasIndex(e => e.BridgeToken).IsUnique();
    }
}
