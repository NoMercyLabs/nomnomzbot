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
using NomNomzBot.Domain.Trust;
using NomNomzBot.Domain.Trust.Entities;

namespace NomNomzBot.Infrastructure.Trust.Persistence;

/// <summary>
/// S-OWN23 — one trust policy per channel. Heat deltas are money-free decimals matching
/// <c>UserTrustScore</c>'s decimal(8,4) so a delta and the score it moves never disagree by rounding;
/// weights and decay rates stay <c>double</c>, the type the calculator does its exponential maths in.
/// </summary>
public class TrustPolicyConfiguration : IEntityTypeConfiguration<TrustPolicy>
{
    public void Configure(EntityTypeBuilder<TrustPolicy> builder)
    {
        builder.HasKey(e => e.Id);
        builder.HasIndex(e => e.BroadcasterId).IsUnique();

        // Defaults match the TrustPolicy initializers so channels with a stored policy get the shipped boosts.
        builder
            .Property(e => e.AffiliateBoost)
            .HasDefaultValue(TrustPolicyDefaults.AffiliateBoost);
        builder.Property(e => e.PartnerBoost).HasDefaultValue(TrustPolicyDefaults.PartnerBoost);
        builder
            .Property(e => e.VerifiedYouTubeBoost)
            .HasDefaultValue(TrustPolicyDefaults.VerifiedYouTubeBoost);

        builder.Property(e => e.HeatDeltaBan).HasPrecision(8, 4);
        builder.Property(e => e.HeatDeltaTimeout).HasPrecision(8, 4);
        builder.Property(e => e.HeatDeltaReportValidated).HasPrecision(8, 4);
        builder.Property(e => e.HeatDeltaAutoModDenied).HasPrecision(8, 4);
        builder.Property(e => e.HeatDeltaFilterHit).HasPrecision(8, 4);
    }
}
