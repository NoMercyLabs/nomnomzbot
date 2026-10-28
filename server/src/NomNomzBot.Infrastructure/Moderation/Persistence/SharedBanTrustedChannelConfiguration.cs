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
