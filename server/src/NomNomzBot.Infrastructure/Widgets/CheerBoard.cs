// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Domain.Identity.Entities;

namespace NomNomzBot.Infrastructure.Widgets;

/// <summary>The wire shape of a live cheer alert, so a widget adds a seed frame up exactly like a live one.</summary>
internal sealed record CheerSeedPayload(
    string? UserId,
    string DisplayName,
    int Bits,
    string Message,
    bool Anonymous
);

/// <summary>
/// The cheer board of one channel: the bits summed per cheerer from the channel's own stored <c>channel.cheer</c>
/// rows since a moment (null means all time), biggest first. `top_cheerers` and the `labels` top_cheerer mode both
/// seed from it.
/// </summary>
internal static class CheerBoard
{
    private const string CheerEventType = "channel.cheer";

    public static async Task<IReadOnlyList<CheerSeedPayload>> LoadAsync(
        IApplicationDbContext db,
        Guid broadcasterId,
        DateTimeOffset? from,
        CancellationToken cancellationToken
    )
    {
        // CreatedAt is the write time, never earlier than the cheer itself, so it only narrows the read. The
        // cheer's true moment is the occurredAt kept in Data (a legacy import rewrites CreatedAt).
        DateTime? writtenFrom = from?.UtcDateTime;
        List<ChannelEvent> rows = await db
            .ChannelEvents.AsNoTracking()
            .Where(e =>
                e.ChannelId == broadcasterId
                && e.Type == CheerEventType
                && (writtenFrom == null || e.CreatedAt >= writtenFrom)
            )
            .ToListAsync(cancellationToken);

        Dictionary<string, CheerSeedPayload> board = new(StringComparer.Ordinal);
        foreach (ChannelEvent row in rows)
        {
            CheerSeedPayload? cheer = ReadCheer(row, from);
            if (cheer is null)
                continue;

            board[cheer.DisplayName] = board.TryGetValue(
                cheer.DisplayName,
                out CheerSeedPayload? seen
            )
                ? seen with
                {
                    Bits = (int)Math.Min((long)seen.Bits + cheer.Bits, int.MaxValue),
                }
                : cheer;
        }

        return [.. board.Values.OrderByDescending(c => c.Bits)];
    }

    private static CheerSeedPayload? ReadCheer(ChannelEvent row, DateTimeOffset? from)
    {
        if (string.IsNullOrEmpty(row.Data))
            return null;

        try
        {
            using JsonDocument document = JsonDocument.Parse(row.Data);
            JsonElement root = document.RootElement;
            DateTimeOffset when =
                root.TryGetProperty("occurredAt", out JsonElement atElement)
                && atElement.TryGetDateTimeOffset(out DateTimeOffset at)
                    ? at
                    : new DateTimeOffset(DateTime.SpecifyKind(row.CreatedAt, DateTimeKind.Utc));
            string? name = EventVariables.ReadString(root, "user");
            int bits = int.TryParse(
                EventVariables.ReadString(root, "bits"),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out int parsed
            )
                ? parsed
                : 0;
            if (string.IsNullOrEmpty(name) || bits <= 0 || (from is not null && when < from))
                return null;

            return new(
                EventVariables.ReadString(root, "user.id") is { Length: > 0 } id ? id : null,
                name,
                bits,
                string.Empty,
                EventVariables.ReadString(root, "anonymous") == "true"
            );
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
