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
using NomNomzBot.Domain.CustomEvents.Entities;

namespace NomNomzBot.Infrastructure.CustomEvents.Persistence;

public class CustomDataSourceConfiguration : IEntityTypeConfiguration<CustomDataSource>
{
    public void Configure(EntityTypeBuilder<CustomDataSource> builder)
    {
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Name).IsRequired().HasMaxLength(50);
        builder.Property(e => e.DisplayName).IsRequired().HasMaxLength(100);
        builder.Property(e => e.SourceKind).IsRequired().HasMaxLength(20);
        builder.Property(e => e.PresetKey).HasMaxLength(50);
        builder.Property(e => e.EndpointUrl).HasMaxLength(500);
        builder.Property(e => e.FieldMapJson).IsRequired().HasDefaultValue("{}");
        builder.Property(e => e.IsEnabled).HasDefaultValue(false);

        builder.HasIndex(e => e.BroadcasterId);
        builder.HasIndex(e => new { e.BroadcasterId, e.Name }).IsUnique();
        builder.HasIndex(e => e.InboundWebhookEndpointId);
    }
}
