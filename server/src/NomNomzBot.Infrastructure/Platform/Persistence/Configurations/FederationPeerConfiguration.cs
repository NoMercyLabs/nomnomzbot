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

public class FederationPeerConfiguration : IEntityTypeConfiguration<FederationPeer>
{
    public void Configure(EntityTypeBuilder<FederationPeer> builder)
    {
        builder.HasKey(e => e.Id);

        builder.Property(e => e.InstanceId).IsRequired().HasMaxLength(36);
        builder.Property(e => e.DisplayName).HasMaxLength(100);
        builder.Property(e => e.BaseUrl).HasMaxLength(2048);
        builder.Property(e => e.DeploymentMode).IsRequired().HasMaxLength(20);
        builder.Property(e => e.TrustState).IsRequired().HasMaxLength(20);

        builder.HasIndex(e => e.InstanceId).IsUnique(); // the peer's stable global identity
        builder.HasIndex(e => e.TrustState);
    }
}
