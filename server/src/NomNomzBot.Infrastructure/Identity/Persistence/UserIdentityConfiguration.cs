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
using NomNomzBot.Domain.Identity.Entities;

namespace NomNomzBot.Infrastructure.Identity.Persistence;

public class UserIdentityConfiguration : IEntityTypeConfiguration<UserIdentity>
{
    public void Configure(EntityTypeBuilder<UserIdentity> builder)
    {
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id).IsRequired();

        builder.Property(e => e.UserId).IsRequired();

        builder.Property(e => e.Provider).IsRequired().HasMaxLength(20);

        builder.Property(e => e.ProviderUserId).IsRequired().HasMaxLength(100);

        builder.Property(e => e.ProviderUsername).IsRequired().HasMaxLength(255);

        builder.Property(e => e.ProviderDisplayName).HasMaxLength(255);

        builder.Property(e => e.ProviderAvatarUrl).HasMaxLength(2048);

        builder.Property(e => e.IsPrimary).IsRequired();

        builder.Property(e => e.LinkedAt).IsRequired();

        // One identity per external account across the whole system.
        builder
            .HasIndex(e => new { e.Provider, e.ProviderUserId })
            .IsUnique()
            .HasDatabaseName("IX_UserIdentity_Provider_ProviderUserId");

        // At most one identity per provider per user (re-link replaces).
        builder
            .HasIndex(e => new { e.UserId, e.Provider })
            .IsUnique()
            .HasDatabaseName("IX_UserIdentity_UserId_Provider");

        builder
            .HasOne(e => e.User)
            .WithMany()
            .HasForeignKey(e => e.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // The user-level login connection in the vault; cleared (not cascaded) if the connection is removed.
        builder
            .HasOne(e => e.Connection)
            .WithMany()
            .HasForeignKey(e => e.ConnectionId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
