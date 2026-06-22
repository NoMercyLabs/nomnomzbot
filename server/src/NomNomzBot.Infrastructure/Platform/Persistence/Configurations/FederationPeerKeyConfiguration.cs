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
