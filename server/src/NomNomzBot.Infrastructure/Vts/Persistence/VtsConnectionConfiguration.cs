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
using NomNomzBot.Domain.Vts.Entities;

namespace NomNomzBot.Infrastructure.Vts.Persistence;

/// <summary>Schema P.19 — one VTS connection per channel; the bridge token is unique platform-wide.</summary>
public class VtsConnectionConfiguration : IEntityTypeConfiguration<VtsConnection>
{
    public void Configure(EntityTypeBuilder<VtsConnection> builder)
    {
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Mode).IsRequired().HasMaxLength(20);
        builder.Property(e => e.Endpoint).IsRequired().HasMaxLength(200);
        builder.Property(e => e.Status).IsRequired().HasMaxLength(20);

        builder.HasIndex(e => e.BroadcasterId).IsUnique();
        builder.HasIndex(e => e.BridgeToken).IsUnique();
    }
}
