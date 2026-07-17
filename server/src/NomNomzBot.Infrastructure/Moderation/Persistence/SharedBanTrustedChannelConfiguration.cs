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

/// <summary>Schema J.9a — the directional inbound-ban trust list.</summary>
public class SharedBanTrustedChannelConfiguration
    : IEntityTypeConfiguration<SharedBanTrustedChannel>
{
    public void Configure(EntityTypeBuilder<SharedBanTrustedChannel> builder)
    {
        builder.HasKey(e => e.Id);

        // Trust is directional and single-entry per partner.
        builder.HasIndex(e => new { e.BroadcasterId, e.TrustedChannelId }).IsUnique();

        // Two FKs to Channels: cascade only the partner side; restrict the owning side so the pair
        // of relationships can never form a multiple-cascade-path cycle on SQL providers.
        builder
            .HasOne(e => e.TrustedChannel)
            .WithMany()
            .HasForeignKey(e => e.TrustedChannelId)
            .OnDelete(DeleteBehavior.Cascade);
        builder
            .HasOne(e => e.Channel)
            .WithMany()
            .HasForeignKey(e => e.BroadcasterId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
