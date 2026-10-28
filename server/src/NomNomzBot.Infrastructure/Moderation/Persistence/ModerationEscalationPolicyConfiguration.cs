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

/// <summary>Schema J.10 — one escalation policy per channel.</summary>
public class ModerationEscalationPolicyConfiguration
    : IEntityTypeConfiguration<ModerationEscalationPolicy>
{
    public void Configure(EntityTypeBuilder<ModerationEscalationPolicy> builder)
    {
        builder.HasKey(e => e.Id);
        builder.HasIndex(e => e.BroadcasterId).IsUnique();
        builder
            .Property(e => e.DefaultTimeoutSeconds)
            .HasDefaultValue(EscalationPolicyDefaults.DefaultTimeoutSeconds);
    }
}
