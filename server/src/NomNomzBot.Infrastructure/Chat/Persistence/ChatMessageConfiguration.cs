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
using NomNomzBot.Domain.Chat.Entities;

namespace NomNomzBot.Infrastructure.Chat.Persistence;

public class ChatMessageConfiguration : IEntityTypeConfiguration<ChatMessage>
{
    public void Configure(EntityTypeBuilder<ChatMessage> builder)
    {
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id).IsRequired().HasMaxLength(255);

        builder.Property(e => e.BroadcasterId).IsRequired();

        builder.Property(e => e.UserId).IsRequired().HasMaxLength(50);

        builder.Property(e => e.Username).IsRequired().HasMaxLength(255);

        builder.Property(e => e.DisplayName).IsRequired().HasMaxLength(255);

        builder.Property(e => e.UserType).IsRequired().HasMaxLength(20);

        builder.Property(e => e.ColorHex).HasMaxLength(7);

        builder.Property(e => e.Message).IsRequired();

        builder.Property(e => e.Fragments).HasColumnType("jsonb").HasDefaultValueSql("'[]'::jsonb");

        builder.Property(e => e.Badges).HasColumnType("jsonb").HasDefaultValueSql("'[]'::jsonb");

        builder.Property(e => e.MessageType).IsRequired().HasMaxLength(50).HasDefaultValue("text");

        builder.Property(e => e.IsCommand).IsRequired();

        builder.Property(e => e.IsCheer).IsRequired();

        builder.Property(e => e.IsHighlighted).IsRequired();

        builder.Property(e => e.ReplyToMessageId).HasMaxLength(255);

        builder.Property(e => e.StreamId).HasMaxLength(50);

        builder
            .HasOne(e => e.Channel)
            .WithMany()
            .HasForeignKey(e => e.BroadcasterId)
            .OnDelete(DeleteBehavior.Cascade);

        // UserId is the Twitch user id (indexed attribute), not an FK to Users.Id.
        builder.HasIndex(e => e.UserId).HasDatabaseName("IX_ChatMessage_UserId");

        builder
            .HasOne(e => e.Stream)
            .WithMany()
            .HasForeignKey(e => e.StreamId)
            .OnDelete(DeleteBehavior.SetNull);

        builder
            .HasIndex(e => new { e.BroadcasterId, e.CreatedAt })
            .HasDatabaseName("IX_ChatMessage_BroadcasterId_CreatedAt");
    }
}
