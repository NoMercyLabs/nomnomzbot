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
using NomNomzBot.Infrastructure.Platform.Persistence;

namespace NomNomzBot.Infrastructure.Tests.Platform.Persistence;

/// <summary>
/// Audit B5 — the <c>AddWidgetOverlayToken</c> migration must backfill every PRE-EXISTING widget with a real,
/// distinct, cryptographically random token (never the empty-string placeholder EF's <c>AddColumn</c> writes,
/// and never one shared value copy-pasted across rows) before the unique index on <c>Widgets.OverlayToken</c>
/// is created. Proved against a POPULATED fixture (a channel with several pre-existing widget rows seeded via
/// raw SQL at the schema as it stood immediately before this migration) — not an empty database, per this
/// project's own hard-won lesson (a prior SQLite-casing incident shipped on an empty-DB-only migration test).
/// </summary>
[Collection("SqliteFileConcurrency")]
public sealed class AddWidgetOverlayTokenMigrationTests : IDisposable
{
    private const string PreviousMigrationId = "20260910040048_RemoveUserPasswordCredential";
    private const string TargetMigrationId = "20260912162355_AddWidgetOverlayToken";

    private static readonly Guid OwnerUserId = Guid.Parse("0192b100-0000-7000-8000-0000000000a1");
    private static readonly Guid ChannelId = Guid.Parse("0192b100-0000-7000-8000-0000000000a2");
    private static readonly Guid WidgetOneId = Guid.Parse("0192b100-0000-7000-8000-0000000000a3");
    private static readonly Guid WidgetTwoId = Guid.Parse("0192b100-0000-7000-8000-0000000000a4");
    private static readonly Guid WidgetThreeId = Guid.Parse("0192b100-0000-7000-8000-0000000000a5");

    private readonly string _dbPath = Path.Combine(
        Path.GetTempPath(),
        $"nomnomz_widget_overlay_token_{Guid.NewGuid():N}.db"
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

    [Fact]
    public async Task Migration_backfills_every_pre_existing_widget_with_a_distinct_real_token()
    {
        // Arrange: migrate up to (but not including) the target migration, then seed — via raw SQL against the
        // schema AS IT STOOD at PreviousMigrationId, since the current AppDbContext model already carries the
        // OverlayToken columns this migration adds — one channel with THREE pre-existing widgets, exactly the
        // populated-fixture shape a real self-host install upgrading through this migration would have.
        await using (AppDbContext seedContext = NewContext())
        {
            IMigrator seedMigrator = seedContext
                .GetInfrastructure()
                .GetRequiredService<IMigrator>();
            await seedMigrator.MigrateAsync(PreviousMigrationId);

            DateTime now = new(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);
            await seedContext.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO "Users"
                    ("Id", "DisplayName", "Username", "UsernameNormalized", "Platform", "Type",
                     "IsAnonymized", "IsBot", "IsLurking", "IsPlatformPrincipal", "PronounManualOverride",
                     "CreatedAt", "UpdatedAt")
                VALUES
                    ({OwnerUserId}, 'Streamer', 'streamer', 'streamer', 'twitch', 'user',
                     0, 0, 0, 0, 0, {now}, {now})
                """
            );
            await seedContext.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO "Channels"
                    ("Id", "OwnerUserId", "Name", "NameNormalized", "OverlayToken", "AnnounceOnConnect",
                     "BillingTierKey", "ContentLabels", "DeploymentMode", "ExternalChannelId",
                     "IsBrandedContent", "IsLive", "IsOnboarded", "Provider", "Status", "StreamDelay",
                     "Tags", "CreatedAt", "UpdatedAt")
                VALUES
                    ({ChannelId}, {OwnerUserId}, 'teststreamer', 'teststreamer', 'channel-wide-legacy-tok', 0,
                     'free', '[]', 'self_host_full', 'ext-1',
                     0, 0, 1, 'twitch', 'active', 0,
                     '[]', {now}, {now})
                """
            );

            const string emptyJsonObject = "{}";
            foreach (
                (Guid id, string name) in new[]
                {
                    (WidgetOneId, "Alerts"),
                    (WidgetTwoId, "Now Playing"),
                    (WidgetThreeId, "Chat Box"),
                }
            )
            {
                await seedContext.Database.ExecuteSqlInterpolatedAsync(
                    $"""
                    INSERT INTO "Widgets"
                        ("Id", "BroadcasterId", "Name", "Framework", "Source", "IsEnabled",
                         "EventSubscriptions", "Settings", "ConfigSchemaVersion", "CreatedAt", "UpdatedAt")
                    VALUES
                        ({id}, {ChannelId}, {name}, 'vanilla', 'custom', 1,
                         '[]', {emptyJsonObject}, 1, {now}, {now})
                    """
                );
            }
        }

        // Act: run the target migration — the empty-string placeholder EF's AddColumn writes would collide on
        // the unique index below if the backfill did not run first, so this must complete without throwing.
        await using (AppDbContext migrateContext = NewContext())
        {
            IMigrator migrator = migrateContext.GetInfrastructure().GetRequiredService<IMigrator>();
            Func<Task> act = () => migrator.MigrateAsync(TargetMigrationId);
            await act.Should()
                .NotThrowAsync(
                    "the backfill must resolve the placeholder collision before the unique index is created"
                );

            await migrator.MigrateAsync();
        }

        // Assert: every pre-existing widget got a REAL, DISTINCT token — never empty, never shared, never
        // derived from the channel-wide token it used to share.
        await using AppDbContext assertContext = NewContext();
        List<string> tokens = await assertContext
            .Widgets.Where(w => w.BroadcasterId == ChannelId)
            .OrderBy(w => w.Name)
            .Select(w => w.OverlayToken)
            .ToListAsync();

        tokens.Should().HaveCount(3);
        foreach (string token in tokens)
        {
            token.Should().NotBeNullOrEmpty();
            token.Should().NotBe("channel-wide-legacy-tok");
        }
        tokens
            .Distinct()
            .Should()
            .HaveCount(3, "every widget must get its OWN token, never a shared backfilled value");

        // The unique index must still do its job going forward: a fresh duplicate insert is rejected.
        await using AppDbContext duplicateContext = NewContext();
        string existingToken = tokens[0];
        const string emptyJsonForDuplicate = "{}";
        Func<Task> insertDuplicate = () =>
            duplicateContext.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO "Widgets"
                    ("Id", "BroadcasterId", "Name", "Framework", "Source", "IsEnabled",
                     "EventSubscriptions", "Settings", "ConfigSchemaVersion", "OverlayToken", "CreatedAt", "UpdatedAt")
                VALUES
                    ({Guid.NewGuid()}, {ChannelId}, 'Duplicate', 'vanilla', 'custom', 1,
                     '[]', {emptyJsonForDuplicate}, 1, {existingToken}, {DateTime.UtcNow}, {DateTime.UtcNow})
                """
            );
        await insertDuplicate
            .Should()
            .ThrowAsync<Exception>("the unique index must still be enforced");
    }
}
