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
using NomNomzBot.Domain.Analytics.Entities;

namespace NomNomzBot.Infrastructure.Platform.Persistence.Configurations;

public class ChannelAnalyticsDailyConfiguration : IEntityTypeConfiguration<ChannelAnalyticsDaily>
{
    public void Configure(EntityTypeBuilder<ChannelAnalyticsDaily> builder)
    {
        builder.HasKey(e => e.Id); // bigint identity

        // One upserted row per (channel, day) — no PII, not soft-deletable, so a true unique.
        builder.HasIndex(e => new { e.BroadcasterId, e.ActivityDate }).IsUnique();
    }
}
