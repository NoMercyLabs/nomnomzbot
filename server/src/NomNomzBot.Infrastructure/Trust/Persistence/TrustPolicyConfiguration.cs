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

        builder.Property(e => e.HeatDeltaBan).HasPrecision(8, 4);
        builder.Property(e => e.HeatDeltaTimeout).HasPrecision(8, 4);
        builder.Property(e => e.HeatDeltaReportValidated).HasPrecision(8, 4);
        builder.Property(e => e.HeatDeltaAutoModDenied).HasPrecision(8, 4);
        builder.Property(e => e.HeatDeltaFilterHit).HasPrecision(8, 4);
    }
}
