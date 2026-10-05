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
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Raids;

namespace NomNomzBot.Infrastructure.Stream.RaidSuggestions;

/// <summary>
/// Builds raid history from journaled EventSub <c>channel.raid</c> rows. The channel event log cannot do this:
/// it only keeps incoming raids. EventSub notifications are journaled without a subject, so their payload is
/// plaintext; a row that is encrypted or not shaped like a raid is skipped, never guessed at.
/// </summary>
internal sealed class RaidHistoryReader(IApplicationDbContext db) : IRaidHistoryReader
{
    private const string RaidEventType = "channel.raid";

    public async Task<Result<RaidHistory>> GetHistoryAsync(
        Guid channelId,
        string ownTwitchUserId,
        CancellationToken cancellationToken = default
    )
    {
        List<RaidRow> rows = await db
            .EventJournals.AsNoTracking()
            .Where(e =>
                e.BroadcasterId == channelId
                && e.EventType == RaidEventType
                && !e.PayloadIsEncrypted
            )
            .Select(e => new RaidRow(e.Payload, e.OccurredAt))
            .ToListAsync(cancellationToken);

        Dictionary<string, RaidStats> outgoing = [];
        Dictionary<string, RaidStats> incoming = [];
        foreach (RaidRow row in rows)
        {
            if (!TryParse(row.Payload, out string? from, out string? to))
                continue;

            if (from == ownTwitchUserId)
                Add(outgoing, to!, row.OccurredAt);
            else if (to == ownTwitchUserId)
                Add(incoming, from!, row.OccurredAt);
        }

        return Result.Success(new RaidHistory(outgoing, incoming));
    }

    private static void Add(
        Dictionary<string, RaidStats> stats,
        string remoteUserId,
        DateTime occurredAt
    )
    {
        DateTimeOffset at = new(DateTime.SpecifyKind(occurredAt, DateTimeKind.Utc));
        stats[remoteUserId] = stats.TryGetValue(remoteUserId, out RaidStats? existing)
            ? new RaidStats(existing.Count + 1, at > existing.LastAt ? at : existing.LastAt)
            : new RaidStats(1, at);
    }

    private static bool TryParse(string payload, out string? from, out string? to)
    {
        from = null;
        to = null;
        try
        {
            JObject json = JObject.Parse(payload);
            from = json.Value<string>("from_broadcaster_user_id");
            to = json.Value<string>("to_broadcaster_user_id");
        }
        catch (JsonException)
        {
            return false;
        }

        return !string.IsNullOrEmpty(from) && !string.IsNullOrEmpty(to);
    }

    private sealed record RaidRow(string Payload, DateTime OccurredAt);
}
