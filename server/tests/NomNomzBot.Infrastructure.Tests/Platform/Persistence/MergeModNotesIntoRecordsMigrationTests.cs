// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Text.Json;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using NomNomzBot.Infrastructure.Platform.Persistence;

namespace NomNomzBot.Infrastructure.Tests.Platform.Persistence;

/// <summary>
/// The <c>MergeModNotesIntoRecords</c> migration folds the two moderation-note stores into one. This proves it
/// on a real SQLite file (the SelfHostLite runtime): a history note (<c>ModerationHistoryEntries</c>, ActionType
/// "note") and a note already in <c>Records</c> (<c>user_note</c>) are seeded BEFORE the migration; afterwards
/// each is in <c>Records</c> exactly once, the moved note keeps its author, date and text and is unpinned, the
/// pre-existing note is untouched, and the history log keeps its non-note rows and loses the note rows.
/// </summary>
[Collection("SqliteFileConcurrency")]
public sealed class MergeModNotesIntoRecordsMigrationTests : IDisposable
{
    private const string PreviousMigrationId = "20261006005632_AddMassBanRunsAsBroadcaster";
    private const string TargetMigrationId = "20261006120504_MergeModNotesIntoRecords";

    private static readonly Guid Broadcaster = Guid.Parse("0192a000-0000-7000-8000-0000000000f1");
    private static readonly Guid Subject = Guid.Parse("0192a000-0000-7000-8000-0000000000f2");
    private static readonly Guid Moderator = Guid.Parse("0192a000-0000-7000-8000-0000000000f3");
    private static readonly DateTime NoteAt = new(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);

    private readonly string _dbPath = Path.Combine(
        Path.GetTempPath(),
        $"nomnomz_merge_notes_{Guid.NewGuid():N}.db"
    );

    private string ConnectionString => $"Data Source={_dbPath};Default Timeout=90";

    public void Dispose()
    {
        using (SqliteConnection ownPool = new(ConnectionString))
            SqliteConnection.ClearPool(ownPool);

        foreach (
            string path in new[]
            {
                _dbPath,
                $"{_dbPath}-wal",
                $"{_dbPath}-shm",
                $"{_dbPath}-journal",
            }
        )
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    private AppDbContext NewContext()
    {
        DbContextOptions<AppDbContext> options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(
                ConnectionString,
                sqliteOptions => sqliteOptions.MigrationsAssembly("NomNomzBot.Migrations.Sqlite")
            )
            .Options;
        return new(options);
    }

    private static string NoteData(string content, bool pinned) =>
        JsonSerializer.Serialize(
            new Dictionary<string, object>
            {
                ["SubjectTwitchUserId"] = "subject-twitch",
                ["Content"] = content,
                ["Pinned"] = pinned,
            }
        );

    [Fact]
    public async Task Migration_moves_history_notes_into_records_and_keeps_existing_notes_once()
    {
        // Arrange: schema as it stood before the migration, seeded with raw SQL (the current model carries
        // later columns, so an EF Add here would not match a genuinely pre-existing install).
        await using (AppDbContext seedContext = NewContext())
        {
            IMigrator seedMigrator = seedContext
                .GetInfrastructure()
                .GetRequiredService<IMigrator>();
            await seedMigrator.MigrateAsync(PreviousMigrationId);

            // The seed rows reference users this raw-SQL fixture does not create; FK checks are per connection.
            await seedContext.Database.OpenConnectionAsync();
            await seedContext.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys = OFF");

            await seedContext.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO "ModerationHistoryEntries"
                    ("Id", "BroadcasterId", "SubjectUserId", "SubjectTwitchUserId", "ActionType",
                     "ModeratorUserId", "ModeratorDisplayName", "Reason", "OccurredAt", "CreatedAt", "UpdatedAt")
                VALUES
                    ({Guid.CreateVersion7()}, {Broadcaster}, {Subject}, 'subject-twitch', 'note',
                     {Moderator}, 'ModUser', 'history note text', {NoteAt}, {NoteAt}, {NoteAt}),
                    ({Guid.CreateVersion7()}, {Broadcaster}, {Subject}, 'subject-twitch', 'ban',
                     {Moderator}, 'ModUser', 'a real ban', {NoteAt}, {NoteAt}, {NoteAt})
                """
            );

            string existing = NoteData("panel note text", pinned: true);
            string authorText = Moderator.ToString();
            await seedContext.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO "Records" ("BroadcasterId", "RecordType", "Data", "UserId", "CreatedAt", "UpdatedAt")
                VALUES ({Broadcaster}, 'user_note', {existing}, {authorText}, {NoteAt}, {NoteAt})
                """
            );
        }

        // Act
        await using (AppDbContext migrateContext = NewContext())
        {
            IMigrator migrator = migrateContext.GetInfrastructure().GetRequiredService<IMigrator>();
            await migrator.MigrateAsync(TargetMigrationId);
        }

        // Assert: read raw so the check does not depend on the current entity model.
        await using SqliteConnection connection = new(ConnectionString);
        await connection.OpenAsync();

        List<(string Data, string UserId, string CreatedAt)> notes = [];
        await using (SqliteCommand command = connection.CreateCommand())
        {
            command.CommandText =
                "SELECT \"Data\", \"UserId\", \"CreatedAt\" FROM \"Records\" WHERE \"RecordType\" = 'user_note' ORDER BY \"Id\"";
            await using SqliteDataReader reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                notes.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2)));
        }

        notes.Should().HaveCount(2, "the panel note and the moved history note, each exactly once");

        JsonElement panel = JsonDocument.Parse(notes[0].Data).RootElement;
        panel.GetProperty("Content").GetString().Should().Be("panel note text");
        panel.GetProperty("Pinned").GetBoolean().Should().BeTrue("the existing note is untouched");

        JsonElement moved = JsonDocument.Parse(notes[1].Data).RootElement;
        moved.GetProperty("Content").GetString().Should().Be("history note text");
        moved.GetProperty("SubjectTwitchUserId").GetString().Should().Be("subject-twitch");
        moved.GetProperty("Pinned").GetBoolean().Should().BeFalse("a history note had no pin");
        notes[1]
            .UserId.Should()
            .Be(Moderator.ToString(), "the author id keeps Guid.ToString() casing");
        notes[1].CreatedAt.Should().StartWith("2026-09-01 12:00:00", "the note keeps its date");

        await using SqliteCommand history = connection.CreateCommand();
        history.CommandText =
            "SELECT \"ActionType\" FROM \"ModerationHistoryEntries\" ORDER BY \"ActionType\"";
        List<string> remaining = [];
        await using (SqliteDataReader reader = await history.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
                remaining.Add(reader.GetString(0));
        }

        remaining.Should().Equal(["ban"], "the note row left the history log; the ban stays");
    }
}
