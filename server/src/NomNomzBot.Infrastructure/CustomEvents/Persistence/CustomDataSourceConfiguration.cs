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
        builder.Property(e => e.LastError).HasMaxLength(1000);
        builder.Property(e => e.LastFieldErrorsJson).HasMaxLength(1000);
        builder.Property(e => e.ConsecutiveFailureCount).HasDefaultValue(0);
        builder.Property(e => e.DisabledReason).HasMaxLength(500);

        builder.HasIndex(e => e.BroadcasterId);
        builder.HasIndex(e => new { e.BroadcasterId, e.Name }).IsUnique();
        builder.HasIndex(e => e.InboundWebhookEndpointId);
    }
}
