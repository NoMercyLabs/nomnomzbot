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

public class CommandCooldownStateConfiguration : IEntityTypeConfiguration<CommandCooldownState>
{
    public void Configure(EntityTypeBuilder<CommandCooldownState> builder)
    {
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedOnAdd();

        builder.Property(e => e.CommandId).IsRequired();
        builder.Property(e => e.BroadcasterId).IsRequired();
        builder.Property(e => e.LastInvokedAt).IsRequired();
        builder.Property(e => e.ExpiresAt).IsRequired();

        // Composite lookup: per-command global cooldown
        builder
            .HasIndex(e => new { e.CommandId, e.ExpiresAt })
            .HasDatabaseName("IX_CommandCooldownState_CommandId_ExpiresAt");

        // Per-user cooldown lookup
        builder
            .HasIndex(e => new
            {
                e.CommandId,
                e.UserId,
                e.ExpiresAt,
            })
            .HasDatabaseName("IX_CommandCooldownState_CommandId_UserId_ExpiresAt");
    }
}
