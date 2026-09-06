// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Infrastructure.Content.Commands;

namespace NomNomzBot.Infrastructure.Tests.Content.Commands;

/// <summary>
/// S-SEED-GUIDCASE: Microsoft.Data.Sqlite binds Guid parameters as canonical uppercase-hyphenated
/// text; SQLite's default text comparison is case-sensitive. A Channels.Id row written in any other
/// case (a raw import, a Postgres-to-SQLite restore) is invisible to a parameterized Guid comparison,
/// so <see cref="DefaultCommandsSeeder"/>'s insert of the channel's default builtins trips
/// "FOREIGN KEY constraint failed" — reproduced here on the real relational SQLite database (not the
/// EF InMemory provider, which enforces no FK at all).
/// </summary>
public sealed class DefaultCommandsSeederGuidCasingTests
{
    private static readonly string[] ExpectedKeys = ["sr", "skip", "queue", "volume", "song"];

    private static Channel MakeChannel(Guid id, string name) =>
        new()
        {
            Id = id,
            OwnerUserId = Guid.NewGuid(),
            Name = name,
            NameNormalized = name.ToLowerInvariant(),
        };

    /// <summary>
    /// DONE-WHEN #1. Before the fix this reproduced the real boot crash verbatim — confirmed by
    /// temporarily disabling <c>NormalizeChannelGuidCasingAsync</c> and re-running this test, which
    /// then failed with exactly <c>Microsoft.Data.Sqlite.SqliteException: SQLite Error 19: 'FOREIGN
    /// KEY constraint failed'</c> wrapped in a <see cref="DbUpdateException"/> — the real failure
    /// mode, not merely "threw something". With the fix restored it passes: no crash, and the actual
    /// state change (every default builtin, enabled) proves the seeder ran to completion.
    /// </summary>
    [Fact]
    public async Task The_seeder_self_heals_the_casing_and_seeds_every_default_builtin()
    {
        Guid channelId = Guid.NewGuid();
        await using DefaultCommandsSeederGuidCasingTestDbContext db =
            DefaultCommandsSeederGuidCasingTestDbContext.New();

        db.Channels.Add(MakeChannel(channelId, "corrupted_channel"));
        await db.SaveChangesAsync();

        // Simulate the real-world corruption: a non-EF write left the PK text in a non-canonical
        // case (lower), same GUID, different text bytes than Microsoft.Data.Sqlite would ever bind.
        await db.Database.ExecuteSqlRawAsync("UPDATE Channels SET Id = lower(Id)");
        db.ChangeTracker.Clear();

        DefaultCommandsSeeder seeder = new(db);

        // Must not throw AND must produce the real state change — every default builtin, enabled.
        await seeder.SeedAsync(channelId);

        List<Domain.Commands.Entities.ChannelBuiltinCommand> seeded = await db
            .ChannelBuiltinCommands.Where(c => c.BroadcasterId == channelId)
            .ToListAsync();

        seeded.Select(c => c.BuiltinKey).Should().BeEquivalentTo(ExpectedKeys);
        seeded.Should().OnlyContain(c => c.IsEnabled);

        // The self-heal must have actually rewritten the stored text back to canonical form —
        // otherwise a second boot would still be sitting on a non-canonical row.
        List<string> rawIds = await db
            .Database.SqlQueryRaw<string>("SELECT Id FROM Channels")
            .ToListAsync();
        rawIds.Should().OnlyContain(id => id == id.ToUpperInvariant());
    }

    [Fact]
    public async Task Normalizing_one_channels_casing_never_lets_its_rows_match_a_different_channel()
    {
        Guid corruptedChannelId = Guid.NewGuid();
        Guid otherChannelId = Guid.NewGuid();

        await using DefaultCommandsSeederGuidCasingTestDbContext db =
            DefaultCommandsSeederGuidCasingTestDbContext.New();

        db.Channels.Add(MakeChannel(corruptedChannelId, "corrupted_channel"));
        db.Channels.Add(MakeChannel(otherChannelId, "other_channel"));
        await db.SaveChangesAsync();

        // Only the first channel's Id gets corrupted; the second is left as EF wrote it.
        await db.Database.ExecuteSqlRawAsync(
            "UPDATE Channels SET Id = lower(Id) WHERE Id = {0}",
            corruptedChannelId
        );
        db.ChangeTracker.Clear();

        DefaultCommandsSeeder seeder = new(db);

        // Seed every channel in one pass (the real startup call shape).
        await seeder.SeedAsync(broadcasterId: null);

        List<Domain.Commands.Entities.ChannelBuiltinCommand> corrupted = await db
            .ChannelBuiltinCommands.Where(c => c.BroadcasterId == corruptedChannelId)
            .ToListAsync();
        List<Domain.Commands.Entities.ChannelBuiltinCommand> other = await db
            .ChannelBuiltinCommands.Where(c => c.BroadcasterId == otherChannelId)
            .ToListAsync();

        // Each channel gets exactly its own 5 rows — the case-insensitive self-heal must not fuse
        // two distinct channels into one tenant, nor let one channel's rows answer for the other's.
        corrupted.Select(c => c.BuiltinKey).Should().BeEquivalentTo(ExpectedKeys);
        other.Select(c => c.BuiltinKey).Should().BeEquivalentTo(ExpectedKeys);
        corrupted.Should().OnlyContain(c => c.BroadcasterId == corruptedChannelId);
        other.Should().OnlyContain(c => c.BroadcasterId == otherChannelId);
        (await db.ChannelBuiltinCommands.CountAsync()).Should().Be(10);
    }
}
