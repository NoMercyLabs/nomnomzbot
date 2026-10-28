// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot. You may use, change and share it for free,
//  for any noncommercial purpose, under the PolyForm Noncommercial License
//  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
//  See LICENSE for details, and TRADEMARKS.md for the name and logo.
//
//  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// -----------------------------------------------------------------------------

using System.Globalization;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Dashboard.Dtos;
using NomNomzBot.Application.Dashboard.Stats;

namespace NomNomzBot.Infrastructure.Dashboard.Stats;

/// <summary>One recorded channel fact (follow, sub, cheer, raid) with its scrubbed JSON payload.</summary>
public sealed record StreamFact(string Type, JsonObject Data)
{
    public const string Anonymous = "Anonymous";

    public int Int(string key) => Data[key] is JsonValue v && v.TryGetValue(out int n) ? n : 0;

    public string? Text(string key) =>
        Data[key] is JsonValue v && v.TryGetValue(out string? s) ? s : null;

    public bool Flag(string key) => Data[key] is JsonValue v && v.TryGetValue(out bool b) && b;

    public string NameOf(string key)
    {
        string? name = Text(key);
        return Flag("isAnonymous") || string.IsNullOrEmpty(name) ? Anonymous : name;
    }

    /// <summary>The platform id of the person the fact is about: viewer, gifter or raider.</summary>
    public string? PersonId() => Text("userId") ?? Text("gifterUserId") ?? Text("fromUserId");

    public DateTime OccurredAt() => TryOccurredAt(Data, out DateTime at) ? at : DateTime.MaxValue;

    public static bool TryOccurredAt(JsonObject data, out DateTime at) =>
        DateTime.TryParse(
            data["occurredAt"]?.GetValue<string>(),
            CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
            out at
        );
}

/// <summary>
/// Reads the channel facts of one stream window once per request and shares them between the metrics that count
/// them. The window is cut by the fact's own <c>occurredAt</c>, because an imported row's <c>CreatedAt</c> is the
/// import time.
/// </summary>
public sealed class StreamFactReader(IApplicationDbContext db, StreamBanFilter bans)
{
    private static readonly string[] FactTypes =
    [
        "channel.follow",
        "channel.subscribe",
        "channel.subscription.message",
        "channel.subscription.gift",
        "channel.cheer",
        "channel.raid",
        "channel.hype_train.end",
    ];

    private StreamWindow? _window;
    private List<StreamFact>? _facts;

    public async Task<List<StreamFact>> OfTypeAsync(
        StreamWindow window,
        string type,
        CancellationToken cancellationToken
    )
    {
        if (!ReferenceEquals(_window, window) || _facts is null)
        {
            _facts = await LoadAsync(window, cancellationToken);
            _window = window;
        }

        return _facts.Where(f => f.Type == type).ToList();
    }

    private async Task<List<StreamFact>> LoadAsync(
        StreamWindow window,
        CancellationToken cancellationToken
    )
    {
        DateTime from = window.From;
        DateTime to = window.To;

        // CreatedAt is the write time and never earlier than the fact, so it is a safe lower bound.
        BannedPeople banned = await bans.ForChannelAsync(window.ChannelId, cancellationToken);
        var rows = await db
            .ChannelEvents.AsNoTracking()
            .Where(e =>
                e.ChannelId == window.ChannelId && FactTypes.Contains(e.Type) && e.CreatedAt >= from
            )
            .Select(e => new { e.Type, e.Data })
            .ToListAsync(cancellationToken);

        // A raid the owner sent is an outgoing raid. Older imports stored those as incoming, so skip them here.
        string? ownerId = await db
            .Channels.AsNoTracking()
            .Where(c => c.Id == window.ChannelId)
            .Select(c => c.ExternalChannelId)
            .FirstOrDefaultAsync(cancellationToken);

        List<StreamFact> facts = [];
        foreach (var row in rows)
        {
            if (JsonNode.Parse(row.Data ?? "{}") is not JsonObject data)
                continue;
            if (!StreamFact.TryOccurredAt(data, out DateTime at) || at < from || at > to)
                continue;
            StreamFact fact = new(row.Type, data);
            if (row.Type == "channel.raid" && ownerId is not null && fact.PersonId() == ownerId)
                continue;
            if (!banned.HasPlatformId(fact.PersonId()))
                facts.Add(fact);
        }

        return facts;
    }
}

/// <summary>Ranks counted names the same way for every list: count descending, then name, capped.</summary>
public static class StreamRanking
{
    public const int ListCap = 5;

    public static List<StreamStatsNamedCountDto> Top(
        IEnumerable<(string Name, int Count)> counted
    ) =>
        counted
            .OrderByDescending(c => c.Count)
            .ThenBy(c => c.Name, StringComparer.Ordinal)
            .Take(ListCap)
            .Select(c => new StreamStatsNamedCountDto { Name = c.Name, Count = c.Count })
            .ToList();

    public static List<StreamStatsNamedCountDto> TopOfNames(IEnumerable<string> names) =>
        Top(names.GroupBy(n => n).Select(g => (g.Key, g.Count())));
}
