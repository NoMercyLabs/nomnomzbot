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
using NomNomzBot.Domain.Analytics.Entities;

namespace NomNomzBot.Infrastructure.Platform.Persistence.Configurations;

public class ViewerEngagementDailyConfiguration : IEntityTypeConfiguration<ViewerEngagementDaily>
{
    public void Configure(EntityTypeBuilder<ViewerEngagementDaily> builder)
    {
        builder.HasKey(e => e.Id); // bigint identity

        builder.HasIndex(e => e.ViewerUserId);
        builder
            .HasIndex(e => new
            {
                e.BroadcasterId,
                e.ViewerUserId,
                e.ActivityDate,
            })
            .IsUnique();
    }
}
