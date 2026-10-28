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
