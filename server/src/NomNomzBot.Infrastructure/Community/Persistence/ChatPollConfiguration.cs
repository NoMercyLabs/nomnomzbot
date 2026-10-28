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
using NomNomzBot.Domain.Community.Entities;

namespace NomNomzBot.Infrastructure.Community.Persistence;

public class ChatPollConfiguration : IEntityTypeConfiguration<ChatPoll>
{
    public void Configure(EntityTypeBuilder<ChatPoll> builder)
    {
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Question).IsRequired().HasMaxLength(200);
        builder.Property(e => e.OptionsJson).IsRequired();
        builder.Property(e => e.Status).IsRequired().HasMaxLength(10);

        // The hot lookups: "the channel's open poll" and the history list.
        builder.HasIndex(e => new { e.BroadcasterId, e.Status });
    }
}

public class ChatPollVoteConfiguration : IEntityTypeConfiguration<ChatPollVote>
{
    public void Configure(EntityTypeBuilder<ChatPollVote> builder)
    {
        builder.HasKey(e => e.Id);
        builder.Property(e => e.VoterUserId).IsRequired().HasMaxLength(64);
        builder.Property(e => e.VoterProvider).IsRequired().HasMaxLength(20);

        // One CURRENT vote per (poll, voter) — a re-vote updates in place, never duplicates.
        builder
            .HasIndex(e => new
            {
                e.PollId,
                e.VoterProvider,
                e.VoterUserId,
            })
            .IsUnique();
    }
}
