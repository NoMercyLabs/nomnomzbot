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
