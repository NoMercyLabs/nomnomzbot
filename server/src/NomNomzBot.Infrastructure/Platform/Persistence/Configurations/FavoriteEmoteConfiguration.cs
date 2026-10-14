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
using NomNomzBot.Domain.Chat.Entities;
using NomNomzBot.Domain.Identity.Entities;

namespace NomNomzBot.Infrastructure.Platform.Persistence.Configurations;

public class FavoriteEmoteConfiguration : IEntityTypeConfiguration<FavoriteEmote>
{
    public void Configure(EntityTypeBuilder<FavoriteEmote> builder)
    {
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Provider).IsRequired().HasMaxLength(20);
        builder.Property(e => e.EmoteId).IsRequired().HasMaxLength(100);
        builder.Property(e => e.Code).IsRequired().HasMaxLength(100);
        builder.Property(e => e.ImageUrl).HasMaxLength(500);

        // The favorites belong to the user and go with them.
        builder
            .HasOne<User>()
            .WithMany()
            .HasForeignKey(e => e.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // One row per user and emote; the same index serves the per-user list read.
        builder
            .HasIndex(e => new
            {
                e.UserId,
                e.Provider,
                e.EmoteId,
            })
            .IsUnique();
    }
}
