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
using Newtonsoft.Json.Linq;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Music.Services;
using NomNomzBot.Domain.Music.ValueObjects;
using NomNomzBot.Domain.Platform.Entities;
using NomNomzBot.Infrastructure.EventStore.LegacyImport;

namespace NomNomzBot.Infrastructure.Music.Replay;

/// <summary>
/// Imports the old bot's own song-request tally — its <c>Records</c> rows of type <c>Spotify</c> — into the
/// ledger, read-only, from the legacy SQLite file.
///
/// <para>
/// This half exists because the journal alone cannot be complete. A request typed as a search phrase
/// (<c>!sr never gonna give you up</c>) only became a track once the old bot asked the provider, and that
/// answer was never journaled — but the old bot wrote the resolved track into its own tally, so its table is
/// the only surviving record of roughly 280 requests. Run this BEFORE the journal backfill: the journal pass
/// reconciles against what this contributed, keeping the larger of the two counts per viewer and track rather
/// than their sum.
/// </para>
///
/// <para>
/// The legacy row stores a bare Spotify song id and nothing else — no title, no artist. That is precisely the
/// weakness this project stopped repeating, and it is why the imported row carries the uri alone and leaves
/// naming to the explicit metadata step. Nothing here calls out to any provider.
/// </para>
/// </summary>
public sealed class LegacySongRequestImporter : ILegacySongRequestImporter
{
    private const string LegacyRecordType = "Spotify";

    private static readonly JsonSerializerOptions HistoryJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly IApplicationDbContext _db;
    private readonly ILegacyDatabaseLocator _locator;
    private readonly ILogger<LegacySongRequestImporter> _logger;

    public LegacySongRequestImporter(
        IApplicationDbContext db,
        ILegacyDatabaseLocator locator,
        ILogger<LegacySongRequestImporter> logger
    )
    {
        _db = db;
        _locator = locator;
        _logger = logger;
    }

    public async Task<Result<LegacySongRequestImportSummary>> ImportAsync(
        Guid broadcasterId,
        bool dryRun = false,
        CancellationToken cancellationToken = default
    )
    {
        Result<string> path = _locator.Resolve();
        if (path.IsFailure)
            return Result.Failure<LegacySongRequestImportSummary>(
                path.ErrorMessage ?? "Legacy database not found.",
                path.ErrorCode ?? "LEGACY_DB_NOT_FOUND"
            );

        HashSet<string> claimed = await LoadClaimedRefsAsync(broadcasterId, cancellationToken);

        int read = 0;
        int imported = 0;
        int alreadyPresent = 0;
        int unreadable = 0;

        // Read-only: the legacy file is the owner's own data and this must never be the thing that damages it.
        SqliteConnectionStringBuilder connectionString = new()
        {
            DataSource = path.Value,
            Mode = SqliteOpenMode.ReadOnly,
        };

        await using SqliteConnection connection = new(connectionString.ToString());
        await connection.OpenAsync(cancellationToken);

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            "SELECT \"Id\", \"UserId\", \"Data\" FROM \"Records\" WHERE \"RecordType\" = $type "
            + "ORDER BY \"Id\" ASC";
        command.Parameters.AddWithValue("$type", LegacyRecordType);

        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            read++;
            string sourceRef = $"legacy:{reader.GetValue(0)}";
            string? userId = reader.IsDBNull(1) ? null : reader.GetString(1);
            string? songId = ReadSongId(reader.IsDBNull(2) ? null : reader.GetString(2));

            if (userId is null || songId is null)
            {
                unreadable++;
                continue;
            }

            if (!claimed.Add(sourceRef))
            {
                alreadyPresent++;
                continue;
            }

            if (dryRun)
            {
                imported++;
                continue;
            }

            _db.Records.Add(
                new Record
                {
                    BroadcasterId = broadcasterId,
                    UserId = userId,
                    RecordType = SongRequestHistory.RecordType,
                    Data = JsonSerializer.Serialize(
                        new SongRequestHistory(
                            $"spotify:track:{songId}",
                            TrackName: string.Empty,
                            Artist: string.Empty,
                            ImageUrl: null,
                            Provider: "spotify",
                            SourceRef: sourceRef
                        ),
                        HistoryJson
                    ),
                }
            );
            imported++;
        }

        if (imported > 0 && !dryRun)
            await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Legacy song-request import on {Tenant}: read {Read}, imported {Imported}, "
                + "already present {Present}, unreadable {Unreadable}{DryRun}",
            broadcasterId,
            read,
            imported,
            alreadyPresent,
            unreadable,
            dryRun ? " (dry run)" : string.Empty
        );

        return Result.Success(
            new LegacySongRequestImportSummary(read, imported, alreadyPresent, unreadable)
        );
    }

    /// <summary>The legacy payload is <c>{ "SongId": "..." }</c> and nothing else.</summary>
    private static string? ReadSongId(string? data)
    {
        if (string.IsNullOrWhiteSpace(data))
            return null;

        try
        {
            string? songId = JObject.Parse(data)["SongId"]?.Value<string>();
            return string.IsNullOrWhiteSpace(songId) ? null : songId;
        }
        catch (Newtonsoft.Json.JsonException)
        {
            return null;
        }
    }

    private async Task<HashSet<string>> LoadClaimedRefsAsync(
        Guid broadcasterId,
        CancellationToken cancellationToken
    )
    {
        List<string> rows = await _db
            .Records.AsNoTracking()
            .Where(r =>
                r.BroadcasterId == broadcasterId && r.RecordType == SongRequestHistory.RecordType
            )
            .Select(r => r.Data)
            .ToListAsync(cancellationToken);

        HashSet<string> refs = new(StringComparer.Ordinal);
        foreach (string row in rows)
        {
            try
            {
                string? sourceRef = JsonSerializer
                    .Deserialize<SongRequestHistory>(row, HistoryJson)
                    ?.SourceRef;
                if (sourceRef is not null)
                    refs.Add(sourceRef);
            }
            catch (JsonException)
            {
                // Unreadable rows claim nothing; worst case one legacy row is imported a second time.
            }
        }

        return refs;
    }
}
