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
        builder.Property(e => e.VerificationSecretCipher).IsRequired().HasMaxLength(512);
        builder.Property(e => e.VerificationSecretNonce).IsRequired().HasMaxLength(255);
        builder.Property(e => e.TargetEventType).HasMaxLength(100);

        builder.HasIndex(e => e.Token).IsUnique(); // the unguessable URL path segment
        builder.HasIndex(e => new { e.BroadcasterId, e.Name });
    }
}
