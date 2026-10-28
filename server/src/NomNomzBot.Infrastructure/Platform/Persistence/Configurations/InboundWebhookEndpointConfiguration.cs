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
using NomNomzBot.Domain.Webhooks.Entities;

namespace NomNomzBot.Infrastructure.Platform.Persistence.Configurations;

public class InboundWebhookEndpointConfiguration : IEntityTypeConfiguration<InboundWebhookEndpoint>
{
    public void Configure(EntityTypeBuilder<InboundWebhookEndpoint> builder)
    {
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Name).IsRequired().HasMaxLength(200);
        builder.Property(e => e.Token).IsRequired().HasMaxLength(64);
        builder.Property(e => e.AdapterKind).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.VerificationSecretEnvelope).IsRequired().HasMaxLength(1024);
        builder.Property(e => e.TargetEventType).HasMaxLength(100);

        builder.HasIndex(e => e.Token).IsUnique(); // the unguessable URL path segment
        builder.HasIndex(e => new { e.BroadcasterId, e.Name });
    }
}
