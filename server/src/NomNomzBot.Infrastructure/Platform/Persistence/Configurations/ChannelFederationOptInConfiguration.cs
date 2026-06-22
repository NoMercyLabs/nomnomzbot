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

public class ChannelFederationOptInConfiguration : IEntityTypeConfiguration<ChannelFederationOptIn>
{
    public void Configure(EntityTypeBuilder<ChannelFederationOptIn> builder)
    {
        builder.HasKey(e => e.Id);

        builder.Property(e => e.OptInType).IsRequired().HasMaxLength(30);
        builder.Property(e => e.Direction).IsRequired().HasMaxLength(10);

        builder.HasIndex(e => e.BroadcasterId);
        builder.HasIndex(e => e.OptInType);
        builder.HasIndex(e => e.IsEnabled);
        // Uniqueness of (channel, peer, type) is service-enforced — soft-deletable, so a plain index.
        builder.HasIndex(e => new
        {
            e.BroadcasterId,
            e.PeerId,
            e.OptInType,
        });
    }
}
