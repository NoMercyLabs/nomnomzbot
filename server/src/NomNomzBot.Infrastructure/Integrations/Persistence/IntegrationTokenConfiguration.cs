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
using NomNomzBot.Domain.Integrations.Entities;

namespace NomNomzBot.Infrastructure.Integrations.Persistence;

public class IntegrationTokenConfiguration : IEntityTypeConfiguration<IntegrationToken>
{
    public void Configure(EntityTypeBuilder<IntegrationToken> builder)
    {
        builder.HasKey(e => e.Id);

        builder.Property(e => e.TokenType).IsRequired().HasMaxLength(10);
        builder.Property(e => e.CipherText).IsRequired();
        builder.Property(e => e.Nonce).HasMaxLength(64);

        builder
            .HasOne(e => e.Connection)
            .WithMany(c => c.Tokens)
            .HasForeignKey(e => e.ConnectionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder
            .HasOne(e => e.Channel)
            .WithMany()
            .HasForeignKey(e => e.BroadcasterId)
            .OnDelete(DeleteBehavior.SetNull);

        // One row per (connection, token-type).
        builder
            .HasIndex(e => new { e.ConnectionId, e.TokenType })
            .IsUnique()
            .HasDatabaseName("IX_IntegrationToken_Connection_TokenType");
    }
}
