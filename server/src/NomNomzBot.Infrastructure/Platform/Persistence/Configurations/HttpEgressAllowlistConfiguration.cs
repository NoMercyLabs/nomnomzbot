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
