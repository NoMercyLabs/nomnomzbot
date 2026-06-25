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

public class ChannelBuiltinCommandConfiguration : IEntityTypeConfiguration<ChannelBuiltinCommand>
{
    public void Configure(EntityTypeBuilder<ChannelBuiltinCommand> builder)
    {
        builder.HasKey(e => e.Id);

        builder.Property(e => e.BroadcasterId).IsRequired();
        builder.Property(e => e.BuiltinKey).IsRequired().HasMaxLength(100);
        builder.Property(e => e.IsEnabled).IsRequired().HasDefaultValue(true);
        builder.Property(e => e.ConfigSchemaVersion).IsRequired().HasDefaultValue(1);

        builder
            .HasIndex(e => new { e.BroadcasterId, e.BuiltinKey })
            .IsUnique()
            .HasDatabaseName("IX_ChannelBuiltinCommand_BroadcasterId_Key");
    }
}
