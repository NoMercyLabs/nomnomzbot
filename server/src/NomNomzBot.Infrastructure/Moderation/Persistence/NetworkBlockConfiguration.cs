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
using NomNomzBot.Domain.Moderation.Entities;

namespace NomNomzBot.Infrastructure.Moderation.Persistence;

/// <summary>Schema — the network-wide block ledger (S-ADMIN-8b).</summary>
public class NetworkBlockConfiguration : IEntityTypeConfiguration<NetworkBlock>
{
    public void Configure(EntityTypeBuilder<NetworkBlock> builder)
    {
        builder.HasKey(e => e.Id);

        // The enforcement gate's own lookup: "is this user network-blocked right now" — one index.
        builder.HasIndex(e => new { e.TargetUserId, e.Status });
    }
}
