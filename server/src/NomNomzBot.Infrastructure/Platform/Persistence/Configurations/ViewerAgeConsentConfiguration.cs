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

public class ViewerAgeConsentConfiguration : IEntityTypeConfiguration<ViewerAgeConsent>
{
    public void Configure(EntityTypeBuilder<ViewerAgeConsent> builder)
    {
        builder.HasKey(e => e.Id);

        builder.Property(e => e.ViewerTwitchUserId).IsRequired().HasMaxLength(50);
        builder.Property(e => e.ConfirmationMethod).IsRequired().HasMaxLength(30);

        // One consent cache per (channel, viewer) — enforced in IAgeConsentService.
        builder.HasIndex(e => new { e.BroadcasterId, e.ViewerUserId });
    }
}
