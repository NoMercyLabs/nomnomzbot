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
using NomNomzBot.Domain.Economy.Entities;

namespace NomNomzBot.Infrastructure.Platform.Persistence.Configurations;

public class GamePlayConfiguration : IEntityTypeConfiguration<GamePlay>
{
    public void Configure(EntityTypeBuilder<GamePlay> builder)
    {
        builder.HasKey(e => e.Id); // long identity

        builder.Property(e => e.Outcome).HasConversion<string>().HasMaxLength(20);

        builder.HasIndex(e => new { e.BroadcasterId, e.GameConfigId });
        builder.HasIndex(e => new { e.BroadcasterId, e.PlayerUserId });
    }
}
