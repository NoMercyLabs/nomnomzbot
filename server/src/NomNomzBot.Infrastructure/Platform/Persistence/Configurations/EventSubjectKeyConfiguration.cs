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
using NomNomzBot.Domain.EventStore.Entities;

namespace NomNomzBot.Infrastructure.Platform.Persistence.Configurations;

/// <summary>
/// Maps the multi-subject journal-event → DEK link (<see cref="EventSubjectKey"/>, schema O.1a). Unique per
/// <c>(EventId, SubjectKeyId)</c>; the <c>SubjectIdHash</c> index serves the erasure planner's "every DEK
/// mapped to this subject" sweep. References (<c>EventId</c> → journal, <c>SubjectKeyId</c> → CryptoKey) are
/// indexed columns without navigations, matching the journal's append-only cross-tenant conventions.
/// </summary>
public class EventSubjectKeyConfiguration : IEntityTypeConfiguration<EventSubjectKey>
{
    public void Configure(EntityTypeBuilder<EventSubjectKey> builder)
    {
        builder.HasKey(e => e.Id);

        builder.Property(e => e.SubjectIdHash).IsRequired().HasMaxLength(64);
        builder.Property(e => e.Role).HasMaxLength(20);

        builder.HasIndex(e => new { e.EventId, e.SubjectKeyId }).IsUnique();
        builder.HasIndex(e => e.SubjectIdHash);
        builder.HasIndex(e => e.SubjectKeyId);
        builder.HasIndex(e => e.BroadcasterId);
    }
}
