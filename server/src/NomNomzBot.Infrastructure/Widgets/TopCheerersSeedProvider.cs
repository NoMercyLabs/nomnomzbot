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
using NomNomzBot.Application.Widgets.Dtos;
using NomNomzBot.Application.Widgets.Services;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Domain.Widgets.Entities;

namespace NomNomzBot.Infrastructure.Widgets;

/// <summary>
/// Gives `top_cheerers` its board back after a reload: one <c>cheer</c> frame per cheerer carrying the bits summed
/// from this channel's own stored <c>channel.cheer</c> rows, in the shape the widget already adds up live
/// (<c>displayName</c> and <c>bits</c>). The widget's <c>range</c> setting picks the window: <c>stream</c> counts from
/// the moment the channel went live (offline gives no frames, the widget shows its empty state), <c>sinceReset</c>
/// counts from the <c>resetAt</c> setting (empty means all time).
/// </summary>
internal sealed class TopCheerersSeedProvider(IApplicationDbContext db, IChannelRegistry registry)
    : IWidgetSeedProvider
{
    private const string CheerEventType = "channel.cheer";
    private const string SinceResetRange = "sinceReset";

    public string NaturalKey => "top_cheerers";

    public async Task<IReadOnlyList<WidgetSeedFrame>> SeedAsync(
        Guid broadcasterId,
        Widget widget,
        CancellationToken cancellationToken
    )
    {
        bool sinceReset = ReadText(widget, "range") == SinceResetRange;
        DateTimeOffset? from = sinceReset
            ? ResetMoment(widget)
            : registry.Get(broadcasterId)?.WentLiveAt;
        if (!sinceReset && from is null)
            return [];

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

        DateTimeOffset now = DateTimeOffset.UtcNow;
        return
        [
            .. board
                .Values.OrderByDescending(c => c.Bits)
                .Select(c => new WidgetSeedFrame("cheer", c, now)),
        ];
    }

    private static DateTimeOffset? ResetMoment(Widget widget) =>
        DateTimeOffset.TryParse(
            ReadText(widget, "resetAt"),
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal,
            out DateTimeOffset reset
        )
            ? reset
            : null;

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
            string? name = ReadString(root, "user");
            int bits = int.TryParse(
                ReadString(root, "bits"),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out int parsed
            )
                ? parsed
                : 0;
            if (string.IsNullOrEmpty(name) || bits <= 0 || (from is not null && when < from))
                return null;

            return new(
                ReadString(root, "user.id") is { Length: > 0 } id ? id : null,
                name,
                bits,
                string.Empty,
                ReadString(root, "anonymous") == "true"
            );
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // The variables JSON stores every value as text; read a number or a string alike.
    private static string? ReadString(JsonElement root, string key) =>
        root.TryGetProperty(key, out JsonElement value)
            ? value.ValueKind switch
            {
                JsonValueKind.String => value.GetString(),
                JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False =>
                    value.GetRawText(),
                _ => null,
            }
            : null;

    // Settings come back from the JSON column as a string or a JsonElement.
    private static string? ReadText(Widget widget, string key)
    {
        if (!widget.Settings.TryGetValue(key, out object? raw))
            return null;

        string? text = raw switch
        {
            string value => value,
            JsonElement { ValueKind: JsonValueKind.String } element => element.GetString(),
            _ => Convert.ToString(raw, CultureInfo.InvariantCulture),
        };
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    /// <summary>The wire shape of a live cheer alert, so the widget adds the seed frame up exactly like a live one.</summary>
    private sealed record CheerSeedPayload(
        string? UserId,
        string DisplayName,
        int Bits,
        string Message,
        bool Anonymous
    );
}
