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

/// <summary>Schema J.6 — custom per-channel chat filters, many per channel, filtered by tenant + enabled flag.</summary>
public class ChatFilterConfiguration : IEntityTypeConfiguration<ChatFilter>
{
    public void Configure(EntityTypeBuilder<ChatFilter> builder)
    {
        builder.HasKey(e => e.Id);
        builder.HasIndex(e => new { e.BroadcasterId, e.IsEnabled });

        builder.Property(e => e.FilterType).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.Action).HasConversion<string>().HasMaxLength(20);
    }
}
