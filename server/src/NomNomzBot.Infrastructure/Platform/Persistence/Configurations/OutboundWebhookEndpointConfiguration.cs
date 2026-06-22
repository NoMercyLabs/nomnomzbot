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

public class OutboundWebhookEndpointConfiguration
    : IEntityTypeConfiguration<OutboundWebhookEndpoint>
{
    public void Configure(EntityTypeBuilder<OutboundWebhookEndpoint> builder)
    {
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Name).IsRequired().HasMaxLength(200);
        builder.Property(e => e.Fqdn).IsRequired().HasMaxLength(253);
        builder.Property(e => e.Path).HasMaxLength(255);
        builder.Property(e => e.SigningSecretCipher).IsRequired().HasMaxLength(512);
        builder.Property(e => e.SigningSecretNonce).IsRequired().HasMaxLength(255);
        builder.Property(e => e.SecondarySigningSecretCipher).HasMaxLength(512);
        builder.Property(e => e.SecondarySigningSecretNonce).HasMaxLength(255);
        builder.Property(e => e.DisabledReason).HasMaxLength(255);

        // SubscribedEventTypesJson / CustomHeadersJson are JSON text columns (the service owns (de)serialization).

        // Uniqueness of (channel, name) is service-enforced — soft-deletable, so a plain index.
        builder.HasIndex(e => new { e.BroadcasterId, e.Name });
    }
}
