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
