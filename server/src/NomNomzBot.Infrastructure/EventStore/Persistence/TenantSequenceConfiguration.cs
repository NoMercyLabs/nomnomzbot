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
using NomNomzBot.Domain.EventStore.Entities;

namespace NomNomzBot.Infrastructure.EventStore.Persistence;

/// <summary>
/// Maps the per-tenant monotonic counter table (schema Q.3). Unique <c>(BroadcasterId, SequenceName)</c> — the
/// row lock that serializes allocation keys on this constraint, so two appends for the same tenant can never
/// hand out the same position.
/// </summary>
public class TenantSequenceConfiguration : IEntityTypeConfiguration<TenantSequence>
{
    public void Configure(EntityTypeBuilder<TenantSequence> builder)
    {
        builder.HasKey(e => e.Id);

        builder.Property(e => e.BroadcasterId).IsRequired();
        builder.HasIndex(e => e.BroadcasterId).HasDatabaseName("IX_TenantSequence_BroadcasterId");

        builder.Property(e => e.SequenceName).IsRequired().HasMaxLength(50);

        builder.Property(e => e.NextValue).IsRequired();
        builder.Property(e => e.UpdatedAt).IsRequired();

        builder
            .HasIndex(e => new { e.BroadcasterId, e.SequenceName })
            .IsUnique()
            .HasDatabaseName("UX_TenantSequence_BroadcasterId_SequenceName");
    }
}
