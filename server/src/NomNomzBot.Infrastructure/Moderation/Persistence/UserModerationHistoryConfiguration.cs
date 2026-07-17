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
using NomNomzBot.Domain.Moderation.Entities;

namespace NomNomzBot.Infrastructure.Moderation.Persistence;

/// <summary>Schema J.4 — the per-viewer moderation rollup projection.</summary>
public class UserModerationHistoryConfiguration : IEntityTypeConfiguration<UserModerationHistory>
{
    public void Configure(EntityTypeBuilder<UserModerationHistory> builder)
    {
        builder.HasKey(e => e.Id);
        builder.HasIndex(e => new { e.BroadcasterId, e.SubjectUserId }).IsUnique();
    }
}
