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
