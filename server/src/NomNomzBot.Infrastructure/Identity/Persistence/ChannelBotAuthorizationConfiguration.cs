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

public class ChannelBotAuthorizationConfiguration
    : IEntityTypeConfiguration<ChannelBotAuthorization>
{
    public void Configure(EntityTypeBuilder<ChannelBotAuthorization> builder)
    {
        builder.HasKey(e => e.Id);

        builder.Property(e => e.BroadcasterId).IsRequired().HasMaxLength(50);

        builder.Property(e => e.AuthorizedBy).HasMaxLength(50);

        builder.Property(e => e.IsActive).HasDefaultValue(true);

        builder.HasIndex(e => e.BroadcasterId).IsUnique();

        builder
            .HasOne(e => e.Channel)
            .WithOne()
            .HasForeignKey<ChannelBotAuthorization>(e => e.BroadcasterId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
