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

namespace NomNomzBot.Infrastructure.EventStore.Persistence;

/// <summary>
/// Maps the append-only journal (schema O.1). <c>bigint</c> identity PK (global append order); unique
/// <c>EventId</c> (idempotent dedupe) and unique <c>(BroadcasterId, StreamPosition)</c> (idempotent replay —
/// a position can never be double-applied per tenant). Not tenant-filtered (nullable BroadcasterId, read
/// across tenants during replay by design).
/// </summary>
public class EventJournalConfiguration : IEntityTypeConfiguration<EventJournal>
{
    public void Configure(EntityTypeBuilder<EventJournal> builder)
    {
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedOnAdd();

        builder.Property(e => e.EventId).IsRequired();
        builder.HasIndex(e => e.EventId).IsUnique().HasDatabaseName("IX_EventJournal_EventId");

        builder.Property(e => e.BroadcasterId);
        builder.HasIndex(e => e.BroadcasterId).HasDatabaseName("IX_EventJournal_BroadcasterId");

        builder.Property(e => e.StreamPosition).IsRequired();
        builder
            .HasIndex(e => new { e.BroadcasterId, e.StreamPosition })
            .IsUnique()
            .HasDatabaseName("UX_EventJournal_BroadcasterId_StreamPosition");

        builder.Property(e => e.EventType).IsRequired().HasMaxLength(150);
        builder.HasIndex(e => e.EventType).HasDatabaseName("IX_EventJournal_EventType");

        builder.Property(e => e.EventVersion).IsRequired();

        builder.Property(e => e.Source).IsRequired().HasMaxLength(30);

        builder.Property(e => e.Payload).IsRequired();
        builder.Property(e => e.PayloadIsEncrypted).IsRequired();
        builder.Property(e => e.SubjectKeyId);
        builder.HasIndex(e => e.SubjectKeyId).HasDatabaseName("IX_EventJournal_SubjectKeyId");

        builder.Property(e => e.CorrelationId);
        builder.HasIndex(e => e.CorrelationId).HasDatabaseName("IX_EventJournal_CorrelationId");
        builder.Property(e => e.CausationId);

        builder.Property(e => e.ActorUserId);
        builder.HasIndex(e => e.ActorUserId).HasDatabaseName("IX_EventJournal_ActorUserId");
        builder.Property(e => e.ActorExternalUserId).HasMaxLength(50);
        builder.Property(e => e.ActorProvider).HasMaxLength(20);

        builder.Property(e => e.Metadata).IsRequired();

        builder.Property(e => e.OccurredAt).IsRequired();
        builder.HasIndex(e => e.OccurredAt).HasDatabaseName("IX_EventJournal_OccurredAt");
        builder.Property(e => e.RecordedAt).IsRequired();
    }
}
