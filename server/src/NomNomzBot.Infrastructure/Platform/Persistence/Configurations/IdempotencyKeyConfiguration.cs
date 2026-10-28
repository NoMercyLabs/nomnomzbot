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
using NomNomzBot.Domain.Platform.Entities;

namespace NomNomzBot.Infrastructure.Platform.Persistence.Configurations;

/// <summary>
/// Maps the scoped idempotency marker (schema §O.4). <c>bigint</c> identity PK; unique
/// <c>(Scope, Key, BroadcasterId)</c> is the dedupe guarantee. Append-only — no soft-delete, no UpdatedAt.
/// </summary>
public class IdempotencyKeyConfiguration : IEntityTypeConfiguration<IdempotencyKey>
{
    public void Configure(EntityTypeBuilder<IdempotencyKey> builder)
    {
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedOnAdd();

        builder.Property(e => e.Scope).IsRequired().HasMaxLength(100);
        builder.Property(e => e.Key).IsRequired().HasMaxLength(255);
        builder.Property(e => e.BroadcasterId);
        builder.Property(e => e.ResultHash).HasMaxLength(64);
        builder.Property(e => e.ExpiresAt).IsRequired();
        builder.Property(e => e.CreatedAt).IsRequired();

        builder
            .HasIndex(e => new
            {
                e.Scope,
                e.Key,
                e.BroadcasterId,
            })
            .IsUnique()
            .HasDatabaseName("UX_IdempotencyKey_Scope_Key_Broadcaster");

        builder.HasIndex(e => e.ExpiresAt).HasDatabaseName("IX_IdempotencyKey_ExpiresAt");
    }
}
