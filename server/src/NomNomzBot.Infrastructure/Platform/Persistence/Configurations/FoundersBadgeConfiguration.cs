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
using NomNomzBot.Domain.Billing.Entities;

namespace NomNomzBot.Infrastructure.Platform.Persistence.Configurations;

public class FoundersBadgeConfiguration : IEntityTypeConfiguration<FoundersBadge>
{
    public void Configure(EntityTypeBuilder<FoundersBadge> builder)
    {
        builder.HasKey(e => e.Id);

        builder.Property(e => e.InviteCode).HasMaxLength(50);

        builder.HasIndex(e => e.BroadcasterId).IsUnique(); // one badge per channel (not soft-deleted)
        builder.HasIndex(e => e.InviteCode);
    }
}
