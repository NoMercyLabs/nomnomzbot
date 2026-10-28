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
using NomNomzBot.Domain.Notifications.Entities;

namespace NomNomzBot.Infrastructure.Notifications.Persistence;

/// <summary>
/// S-OWN22 T2 — persisted dismissals of action-required inbox items. One live row per
/// (channel, item key): the unique index is filtered to non-soft-deleted rows, matching the
/// <c>IntegrationConnection</c> convention, so a soft-deleted dismissal never blocks re-dismissing
/// a resurfaced item.
/// </summary>
public class ActionRequiredDismissalConfiguration
    : IEntityTypeConfiguration<ActionRequiredDismissal>
{
    public void Configure(EntityTypeBuilder<ActionRequiredDismissal> builder)
    {
        builder.HasKey(e => e.Id);

        builder.Property(e => e.ItemKey).IsRequired().HasMaxLength(200);

        builder
            .HasIndex(e => new { e.ChannelId, e.ItemKey })
            .IsUnique()
            .HasDatabaseName("IX_ActionRequiredDismissal_Channel_ItemKey_Live")
            .HasFilter("\"DeletedAt\" IS NULL");
    }
}
