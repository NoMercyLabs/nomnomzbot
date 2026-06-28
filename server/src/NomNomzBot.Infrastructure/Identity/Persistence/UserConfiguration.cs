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

public class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id).IsRequired();

        builder.Property(e => e.TwitchUserId).HasMaxLength(50);

        builder.HasIndex(e => e.TwitchUserId).IsUnique().HasDatabaseName("IX_User_TwitchUserId");

        builder.Property(e => e.Platform).IsRequired().HasMaxLength(20);

        builder.Property(e => e.Username).IsRequired().HasMaxLength(255);

        builder.Property(e => e.UsernameNormalized).IsRequired().HasMaxLength(255);

        builder.HasIndex(e => e.UsernameNormalized).HasDatabaseName("IX_User_UsernameNormalized");

        builder.Property(e => e.EmailCipher).HasMaxLength(512);

        builder.Property(e => e.DisplayName).IsRequired().HasMaxLength(255);

        builder.Property(e => e.NickName).HasMaxLength(255);

        builder.Property(e => e.Timezone).HasMaxLength(50);

        builder.Property(e => e.Description).HasMaxLength(500);

        builder.Property(e => e.ProfileImageUrl).HasMaxLength(2048);

        builder.Property(e => e.OfflineImageUrl).HasMaxLength(2048);

        builder.Property(e => e.Color).HasMaxLength(7);

        builder.Property(e => e.BroadcasterType).IsRequired().HasMaxLength(50).HasDefaultValue("");

        builder.Property(e => e.Enabled).HasDefaultValue(true);

        builder.Property(e => e.PronounManualOverride).IsRequired();

        builder.Property(e => e.PronounId);

        builder
            .HasOne(e => e.Pronoun)
            .WithMany()
            .HasForeignKey(e => e.PronounId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.Property(e => e.AltPronounId);

        builder
            .HasOne(e => e.AltPronoun)
            .WithMany()
            .HasForeignKey(e => e.AltPronounId)
            .OnDelete(DeleteBehavior.SetNull);

        // Inverse of Channel.User (one-to-one; the channel's OwnerUserId FK targets this user).
        builder
            .HasOne(e => e.Channel)
            .WithOne(c => c.User)
            .HasForeignKey<Channel>(c => c.OwnerUserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
