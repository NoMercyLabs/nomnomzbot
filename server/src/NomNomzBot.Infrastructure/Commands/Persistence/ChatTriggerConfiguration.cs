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
using NomNomzBot.Domain.Commands.Entities;

namespace NomNomzBot.Infrastructure.Commands.Persistence;

public class ChatTriggerConfiguration : IEntityTypeConfiguration<ChatTrigger>
{
    public void Configure(EntityTypeBuilder<ChatTrigger> builder)
    {
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Pattern).IsRequired().HasMaxLength(200);
        builder.Property(e => e.MatchType).IsRequired().HasMaxLength(20);
        builder.Property(e => e.Response).HasMaxLength(500);

        // The registry's cache load: one channel's enabled triggers.
        builder.HasIndex(e => new { e.BroadcasterId, e.IsEnabled });
    }
}
