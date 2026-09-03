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
