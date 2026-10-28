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
using NomNomzBot.Domain.Platform.Entities;

namespace NomNomzBot.Infrastructure.Platform.Persistence.Configurations;

public class HttpEgressAllowlistConfiguration : IEntityTypeConfiguration<HttpEgressAllowlist>
{
    public void Configure(EntityTypeBuilder<HttpEgressAllowlist> builder)
    {
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Fqdn).IsRequired().HasMaxLength(253);
        builder.Property(e => e.AllowedMethods).IsRequired().HasMaxLength(100);
        builder.Property(e => e.PathPrefix).HasMaxLength(255);

        builder.HasIndex(e => e.BroadcasterId);
        builder.HasIndex(e => e.IsEnabled);
        // The egress guard resolves by (tenant, host) — soft-deletable, so a plain index (service-enforced uniqueness).
        builder.HasIndex(e => new { e.BroadcasterId, e.Fqdn });
    }
}
