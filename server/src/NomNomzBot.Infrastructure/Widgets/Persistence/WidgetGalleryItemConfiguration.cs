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
using NomNomzBot.Domain.Widgets.Entities;
using NomNomzBot.Infrastructure.Platform.Persistence.Converters;

namespace NomNomzBot.Infrastructure.Widgets.Persistence;

/// <summary>
/// GLOBAL catalogue table — no tenant scope (no <c>BroadcasterId</c>, no tenant query filter); the composing
/// soft-delete filter is applied centrally in <c>AppDbContext</c>. Reads are unscoped/public; writes are IAM-gated.
/// </summary>
public class WidgetGalleryItemConfiguration : IEntityTypeConfiguration<WidgetGalleryItem>
{
    public void Configure(EntityTypeBuilder<WidgetGalleryItem> builder)
    {
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Name).IsRequired().HasMaxLength(255);
        builder.Property(e => e.Framework).IsRequired().HasMaxLength(20);
        builder.Property(e => e.TrustTier).IsRequired().HasMaxLength(20);
        builder.Property(e => e.SourceKind).IsRequired().HasMaxLength(20);
        builder.Property(e => e.ReviewStatus).IsRequired().HasMaxLength(20);

        builder.Property(e => e.NaturalKey).HasMaxLength(100);
        builder.Property(e => e.SubmitterTwitchUserId).HasMaxLength(50);
        builder.Property(e => e.SubmitterDisplayNameSnapshot).HasMaxLength(255);
        builder.Property(e => e.GitHubRepoUrl).HasMaxLength(2048);
        builder.Property(e => e.PinnedCommitSha).HasMaxLength(40);
        builder.Property(e => e.PinnedTag).HasMaxLength(100);

        builder
            .Property(e => e.DefaultSettings)
            .HasConversion(
                JsonValueConverter.Converter<Dictionary<string, object>>(),
                JsonValueConverter.Comparer<Dictionary<string, object>>()
            );

        builder
            .Property(e => e.DefaultEventSubscriptions)
            .HasConversion(
                JsonValueConverter.Converter<List<string>>(),
                JsonValueConverter.Comparer<List<string>>()
            );

        builder.HasIndex(e => e.TrustTier);
        builder.HasIndex(e => e.ReviewStatus);
        builder.HasIndex(e => e.SubmitterTwitchUserId);
        builder.HasIndex(e => e.NaturalKey).IsUnique();

        // One catalogue row per pinned (repo, commit) — nulls (the in-repo first-party rows) are distinct, so the
        // seeded catalogue never collides here; its idempotency key is NaturalKey above.
        builder.HasIndex(e => new { e.GitHubRepoUrl, e.PinnedCommitSha }).IsUnique();
    }
}
