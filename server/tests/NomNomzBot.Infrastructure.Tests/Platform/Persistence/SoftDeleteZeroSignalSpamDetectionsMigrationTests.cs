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
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using NomNomzBot.Domain.Moderation.Entities;
using NomNomzBot.Domain.Moderation.SpamDefense;
using NomNomzBot.Infrastructure.Platform.Persistence;

namespace NomNomzBot.Infrastructure.Tests.Platform.Persistence;

/// <summary>
/// The old rule stored a "nothing fired" row for every message. The upgrade soft-deletes exactly those
/// rows, leaves every real verdict alone, and writes a DeletedAt the model reads back. Runs the real
/// SQLite migration chain over a populated database.
/// </summary>
[Collection("SqliteFileConcurrency")]
public sealed class SoftDeleteZeroSignalSpamDetectionsMigrationTests : IDisposable
{
    private const string PreviousMigrationId = "20261011030801_AddRaidAssessments";
    private const string LegacyReason = "Established viewer; nothing fired.";

    private static readonly Guid Channel = Guid.Parse("0199c000-0000-7000-8000-0000000000e1");

    private readonly string _dbPath = Path.Combine(
        Path.GetTempPath(),
        $"nomnomz_zero_signal_{Guid.NewGuid():N}.db"
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

    private static SpamDetection Row(
        string reason,
        SpamConfidence confidence,
        SpamOutcome wouldHaveBeen
    ) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            BroadcasterId = Channel,
            SubjectPlatformUserId = "viewer-1",
            SubjectDisplayName = "Viewer",
            Provider = "twitch",
            MessageId = Guid.NewGuid().ToString(),
            MessageText = "hello",
            Skeleton = "hello",
            Signals = string.Empty,
            Confidence = confidence,
            Tier = SpamTrustTier.Untrusted,
            Outcome = SpamOutcome.None,
            WouldHaveBeen = wouldHaveBeen,
            WasDryRun = true,
            Reason = reason,
            DetectedAt = new DateTime(2026, 9, 20, 12, 0, 0, DateTimeKind.Utc),
        };

    [Fact]
    public async Task Upgrade_soft_deletes_only_the_zero_signal_rows()
    {
        SpamDetection legacy = Row(LegacyReason, SpamConfidence.Zero, SpamOutcome.Flag);
        SpamDetection realFlag = Row("Promo shape.", SpamConfidence.Low, SpamOutcome.Flag);
        SpamDetection sameReasonButSignal = Row(LegacyReason, SpamConfidence.Low, SpamOutcome.Flag);
        SpamDetection alreadyDeleted = Row(LegacyReason, SpamConfidence.Zero, SpamOutcome.Flag);
        DateTime earlier = new(2026, 10, 1, 8, 0, 0, DateTimeKind.Utc);
        alreadyDeleted.DeletedAt = earlier;

        await using (AppDbContext seed = NewContext())
        {
            IMigrator migrator = seed.GetInfrastructure().GetRequiredService<IMigrator>();
            await migrator.MigrateAsync(PreviousMigrationId);
            await seed.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys = OFF;");
            seed.SpamDetections.AddRange(legacy, realFlag, sameReasonButSignal, alreadyDeleted);
            await seed.SaveChangesAsync();
        }

        DateTime before = DateTime.UtcNow.AddMinutes(-1);
        await using (AppDbContext migrate = NewContext())
            await migrate.Database.MigrateAsync();

        await using AppDbContext read = NewContext();
        Dictionary<Guid, SpamDetection> rows = await read
            .SpamDetections.IgnoreQueryFilters()
            .ToDictionaryAsync(d => d.Id);

        rows[legacy.Id].DeletedAt.Should().NotBeNull("the zero-signal row is retired");
        rows[legacy.Id]
            .DeletedAt!.Value.Should()
            .BeAfter(before)
            .And.BeBefore(DateTime.UtcNow.AddMinutes(1));
        rows[realFlag.Id].DeletedAt.Should().BeNull("a real verdict stays");
        rows[sameReasonButSignal.Id]
            .DeletedAt.Should()
            .BeNull("a fired signal stays whatever its reason says");
        rows[alreadyDeleted.Id]
            .DeletedAt.Should()
            .Be(earlier, "an earlier delete keeps its own time");
    }
}
