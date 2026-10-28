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
using NomNomzBot.Domain.Federation.Entities;

namespace NomNomzBot.Infrastructure.Platform.Persistence.Configurations;

public class FederationPeerKeyConfiguration : IEntityTypeConfiguration<FederationPeerKey>
{
    public void Configure(EntityTypeBuilder<FederationPeerKey> builder)
    {
        builder.HasKey(e => e.Id);

        builder.Property(e => e.PublicKey).IsRequired();
        builder.Property(e => e.Algorithm).IsRequired().HasMaxLength(30);
        builder.Property(e => e.KeyId).IsRequired().HasMaxLength(64);

        builder.HasIndex(e => e.PeerId);
        builder.HasIndex(e => e.KeyId);
        builder.HasIndex(e => e.IsActive);
        builder.HasIndex(e => new { e.PeerId, e.KeyId }).IsUnique(); // one key version per peer
    }
}
