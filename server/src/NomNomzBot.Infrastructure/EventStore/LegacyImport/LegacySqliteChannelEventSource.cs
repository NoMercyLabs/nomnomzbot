// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Runtime.CompilerServices;
using Microsoft.Data.Sqlite;

namespace NomNomzBot.Infrastructure.EventStore.LegacyImport;

/// <summary>
/// Reads <c>ChannelEvents</c> rows from a legacy NoMercy bot SQLite file (<c>database.sqlite</c>), oldest-first by
/// the row <c>CreatedAt</c>. Opens the file READ-ONLY (<c>Mode=ReadOnly</c>) so the import never mutates the source
/// — it is the owner's live legacy data. Streams rows one at a time (a single forward <c>DbDataReader</c>) so the
/// 40k+ row history never sits in memory at once.
/// </summary>
public sealed class LegacySqliteChannelEventSource : ILegacyChannelEventSource
{
    private readonly string _databasePath;

    public LegacySqliteChannelEventSource(string databasePath) => _databasePath = databasePath;

    public async IAsyncEnumerable<LegacyChannelEventRow> ReadAllAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    )
    {
        SqliteConnectionStringBuilder builder = new()
        {
            DataSource = _databasePath,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false,
        };

        await using SqliteConnection connection = new(builder.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            "SELECT \"Id\", \"ChannelId\", \"UserId\", \"Type\", \"Data\", \"CreatedAt\" "
            + "FROM \"ChannelEvents\" ORDER BY \"CreatedAt\" ASC, \"Id\" ASC";

        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            yield return new LegacyChannelEventRow(
                Id: reader.GetString(0),
                ChannelId: reader.IsDBNull(1) ? null : reader.GetString(1),
                UserId: reader.IsDBNull(2) ? null : reader.GetString(2),
                Type: reader.GetString(3),
                Data: reader.IsDBNull(4) ? null : reader.GetString(4),
                CreatedAt: reader.GetDateTime(5)
            );
        }
    }
}
