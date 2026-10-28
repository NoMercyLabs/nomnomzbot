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
using NomNomzBot.Domain.Widgets.Entities;

namespace NomNomzBot.Infrastructure.Widgets.Persistence;

/// <summary>GLOBAL, append-only review/pin history — no tenant scope, no soft-delete filter.</summary>
public class WidgetGallerySubmissionEventConfiguration
    : IEntityTypeConfiguration<WidgetGallerySubmissionEvent>
{
    public void Configure(EntityTypeBuilder<WidgetGallerySubmissionEvent> builder)
    {
        builder.HasKey(e => e.Id);

        builder.Property(e => e.ToStatus).IsRequired().HasMaxLength(20);
        builder.Property(e => e.FromStatus).HasMaxLength(20);
        builder.Property(e => e.NewPinnedCommitSha).HasMaxLength(40);

        builder.HasIndex(e => e.GalleryItemId);
        builder.HasIndex(e => e.OccurredAt);
    }
}
