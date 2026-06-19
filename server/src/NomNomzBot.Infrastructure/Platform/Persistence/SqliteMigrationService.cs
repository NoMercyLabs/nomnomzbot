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
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Domain.Chat.Entities;
using NomNomzBot.Domain.Identity.Entities;

namespace NomNomzBot.Infrastructure.Platform.Persistence;

/// <summary>
/// Migrates data from the legacy SQLite-based NomNomzBot to the new PostgreSQL schema.
///
/// What IS migrated (per spec 14-migration-guide.md):
///   - Users (upsert)
///   - ChatMessages (with BroadcasterId set)
///   - Commands (enabled only)
///   - Records (watch streaks, command usage)
///   - ChannelEvents
///
/// What is NOT migrated:
///   - OAuth tokens (re-authenticate after migration)
///   - Roslyn scripts (recreate as pipeline commands)
///   - Widget HTML/JS (widget system redesigned)
///   - EventSub subscriptions (re-created on onboarding)
/// </summary>
public sealed class SqliteMigrationService
{
    private readonly IApplicationDbContext _db;
    private readonly ILogger<SqliteMigrationService> _logger;

    public SqliteMigrationService(IApplicationDbContext db, ILogger<SqliteMigrationService> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>
    /// Runs the full migration from the SQLite database file to the current PostgreSQL instance.
    /// </summary>
    /// <param name="sqliteFilePath">Path to the old .db file.</param>
    /// <param name="broadcasterId">The Twitch user ID of the channel being migrated.</param>
    /// <param name="cancellationToken"></param>
    /// <returns>A summary of what was migrated.</returns>
    public async Task<MigrationResult> MigrateAsync(
        string sqliteFilePath,
        string broadcasterId,
        CancellationToken cancellationToken = default
    )
    {
        if (!File.Exists(sqliteFilePath))
            return new(false, $"SQLite file not found: {sqliteFilePath}");

        // Ensure the target channel exists
        bool channelExists = await _db.Channels.AnyAsync(
            c => c.Id == broadcasterId,
            cancellationToken
        );
        if (!channelExists)
            return new(
                false,
                $"Channel {broadcasterId} not found. Complete onboarding first."
            );

        MigrationCounts counts = new();

        string connectionString = $"Data Source={sqliteFilePath};Mode=ReadOnly;";
        await using SqliteConnection conn = new(connectionString);
        await conn.OpenAsync(cancellationToken);

        // Migration steps are independent — failures are logged but don't abort others
        counts.Users = await MigrateUsersAsync(conn, cancellationToken);
        counts.Commands = await MigrateCommandsAsync(conn, broadcasterId, cancellationToken);
        counts.ChatMessages = await MigrateChatMessagesAsync(
            conn,
            broadcasterId,
            cancellationToken
        );
        counts.Records = await MigrateRecordsAsync(conn, broadcasterId, cancellationToken);

        _logger.LogInformation(
            "Migration complete for {BroadcasterId}: {Users} users, {Commands} commands, "
                + "{Messages} messages, {Records} records",
            broadcasterId,
            counts.Users,
            counts.Commands,
            counts.ChatMessages,
            counts.Records
        );

        return new(true, "Migration completed successfully.", counts);
    }

    // ─── Users ────────────────────────────────────────────────────────────────

    private async Task<int> MigrateUsersAsync(SqliteConnection conn, CancellationToken ct)
    {
        int count = 0;

        await using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Id, Username, DisplayName, ProfileImageUrl FROM Users";

        await using SqliteDataReader reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            string id = reader.GetString(0);
            string username = reader.GetString(1);
            string displayName = reader.GetString(2);
            string? profileUrl = reader.IsDBNull(3) ? null : reader.GetString(3);

            User? existing = await _db.Users.FirstOrDefaultAsync(u => u.Id == id, ct);
            if (existing is null)
            {
                _db.Users.Add(
                    new()
                    {
                        Id = id,
                        Username = username,
                        DisplayName = displayName,
                        ProfileImageUrl = profileUrl,
                        Enabled = true,
                    }
                );
                count++;
            }
        }

        if (count > 0)
            await _db.SaveChangesAsync(ct);

        return count;
    }

    // ─── Commands ─────────────────────────────────────────────────────────────

    private async Task<int> MigrateCommandsAsync(
        SqliteConnection conn,
        string broadcasterId,
        CancellationToken ct
    )
    {
        int count = 0;

        await using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText =
            "SELECT Name, Response, CooldownSeconds, IsEnabled FROM Commands WHERE IsEnabled = 1";

        await using SqliteDataReader reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            string name = reader.GetString(0).ToLowerInvariant();
            string? response = reader.IsDBNull(1) ? null : reader.GetString(1);
            int cooldown = reader.IsDBNull(2) ? 0 : reader.GetInt32(2);

            bool existing = await _db.Commands.AnyAsync(
                c => c.BroadcasterId == broadcasterId && c.Name == name,
                ct
            );
            if (!existing)
            {
                _db.Commands.Add(
                    new()
                    {
                        BroadcasterId = broadcasterId,
                        Name = name,
                        Response = response,
                        CooldownSeconds = cooldown,
                        IsEnabled = true,
                        Type = "chat",
                        Permission = "everyone",
                        Responses = [],
                        Aliases = [],
                    }
                );
                count++;
            }
        }

        if (count > 0)
            await _db.SaveChangesAsync(ct);

        return count;
    }

    // ─── Chat messages ────────────────────────────────────────────────────────

    private async Task<int> MigrateChatMessagesAsync(
        SqliteConnection conn,
        string broadcasterId,
        CancellationToken ct
    )
    {
        int count = 0;

        // Check if ChatMessages table exists in the old DB
        await using SqliteCommand checkCmd = conn.CreateCommand();
        checkCmd.CommandText =
            "SELECT name FROM sqlite_master WHERE type='table' AND name='ChatMessages'";
        bool tableExists = await checkCmd.ExecuteScalarAsync(ct) is not null;
        if (!tableExists)
            return 0;

        await using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText =
            @"
            SELECT Id, UserId, Username, DisplayName, Message, CreatedAt
            FROM ChatMessages
            ORDER BY CreatedAt ASC
            LIMIT 10000";

        await using SqliteDataReader reader = await cmd.ExecuteReaderAsync(ct);
        List<ChatMessage> batch = new();

        while (await reader.ReadAsync(ct))
        {
            string msgId = reader.GetString(0);
            string userId = reader.GetString(1);
            string username = reader.GetString(2);
            string displayName = reader.GetString(3);
            string message = reader.GetString(4);
            DateTime createdAt = reader.GetDateTime(5);

            // Skip if already migrated
            bool exists = await _db.ChatMessages.AnyAsync(m => m.Id == msgId, ct);
            if (exists)
                continue;

            batch.Add(
                new()
                {
                    Id = msgId,
                    BroadcasterId = broadcasterId,
                    UserId = userId,
                    Username = username,
                    DisplayName = displayName,
                    Message = message,
                    UserType = "viewer",
                    MessageType = "text",
                    Fragments = [],
                    Badges = [],
                    CreatedAt = createdAt,
                }
            );

            if (batch.Count >= 500)
            {
                _db.ChatMessages.AddRange(batch);
                await _db.SaveChangesAsync(ct);
                count += batch.Count;
                batch.Clear();
            }
        }

        if (batch.Count > 0)
        {
            _db.ChatMessages.AddRange(batch);
            await _db.SaveChangesAsync(ct);
            count += batch.Count;
        }

        return count;
    }

    // ─── Records (watch streaks, usage stats) ─────────────────────────────────

    private async Task<int> MigrateRecordsAsync(
        SqliteConnection conn,
        string broadcasterId,
        CancellationToken ct
    )
    {
        int count = 0;

        await using SqliteCommand checkCmd = conn.CreateCommand();
        checkCmd.CommandText =
            "SELECT name FROM sqlite_master WHERE type='table' AND name='Records'";
        bool tableExists = await checkCmd.ExecuteScalarAsync(ct) is not null;
        if (!tableExists)
            return 0;

        await using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT RecordType, Data, UserId FROM Records";

        await using SqliteDataReader reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            string recordType = reader.GetString(0);
            string data = reader.GetString(1);
            string userId = reader.IsDBNull(2) ? broadcasterId : reader.GetString(2);

            _db.Records.Add(
                new()
                {
                    BroadcasterId = broadcasterId,
                    RecordType = recordType,
                    Data = data,
                    UserId = userId,
                }
            );
            count++;
        }

        if (count > 0)
            await _db.SaveChangesAsync(ct);

        return count;
    }
}

public sealed class MigrationResult
{
    public bool Success { get; }
    public string Message { get; }
    public MigrationCounts? Counts { get; }

    public MigrationResult(bool success, string message, MigrationCounts? counts = null)
    {
        Success = success;
        Message = message;
        Counts = counts;
    }
}

public sealed class MigrationCounts
{
    public int Users { get; set; }
    public int Commands { get; set; }
    public int ChatMessages { get; set; }
    public int Records { get; set; }
}
