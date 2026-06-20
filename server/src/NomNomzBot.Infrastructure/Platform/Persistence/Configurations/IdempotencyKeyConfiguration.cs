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
