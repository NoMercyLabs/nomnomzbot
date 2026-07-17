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
using NomNomzBot.Domain.Moderation.Entities;

namespace NomNomzBot.Infrastructure.Moderation.Persistence;

/// <summary>Schema J.5 — the per-viewer trust + heat projection.</summary>
public class UserTrustScoreConfiguration : IEntityTypeConfiguration<UserTrustScore>
{
    public void Configure(EntityTypeBuilder<UserTrustScore> builder)
    {
        builder.HasKey(e => e.Id);
        builder.Property(e => e.TrustScore).HasPrecision(8, 4);
        builder.Property(e => e.HeatScore).HasPrecision(8, 4);
        builder.HasIndex(e => new { e.BroadcasterId, e.SubjectUserId }).IsUnique();
        builder.HasIndex(e => e.ComputedAt);
    }
}
