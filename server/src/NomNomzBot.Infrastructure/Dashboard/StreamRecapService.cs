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
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Dashboard.Dtos;
using NomNomzBot.Application.Dashboard.Services;
using NomNomzBot.Domain.Music.ValueObjects;

namespace NomNomzBot.Infrastructure.Dashboard;

/// <summary>
/// Counts one stream's recap from recorded rows inside the stream window. Channel facts come from the channel
/// event log (names are already scrubbed to display names); the window is cut by the fact's own <c>occurredAt</c>,
/// because an imported row's <c>CreatedAt</c> is the import time. Redemptions, song requests and chat come from
/// their own tables. A stat the bot does not record is not in the DTO.
/// </summary>
public sealed class StreamRecapService(IApplicationDbContext db, TimeProvider timeProvider)
    : IStreamRecapService
{
    private const int ListCap = 5;
    private const string AnonymousName = "Anonymous";
    private static readonly string[] FactTypes =
    [
        "channel.follow",
        "channel.subscribe",
        "channel.subscription.message",
        "channel.subscription.gift",
        "channel.cheer",
        "channel.raid",
    ];

    private static readonly JsonSerializerOptions CamelCase = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private sealed record Fact(string Type, JsonObject Data);

    public async Task<Result<StreamRecapDto>> GetRecapAsync(
        Guid broadcasterId,
        string? streamId,
        CancellationToken cancellationToken = default
    )
    {
        Domain.Stream.Entities.Stream? stream = await FindStreamAsync(
            broadcasterId,
            streamId,
            cancellationToken
        );
        if (stream?.StartedAt is not { } startedAt)
            return Result.Failure<StreamRecapDto>("Stream not found.", "NOT_FOUND");

        DateTimeOffset now = timeProvider.GetUtcNow();
        DateTimeOffset end = stream.EndedAt ?? now;
        DateTime from = startedAt.UtcDateTime;
        DateTime to = end.UtcDateTime;

        List<Fact> facts = await LoadFactsAsync(broadcasterId, from, to, cancellationToken);

        StreamRecapDto recap = new()
        {
            Stream = new()
            {
                Id = stream.Id,
                StartedAt = startedAt,
                EndedAt = stream.EndedAt,
                DurationSeconds = (long)Math.Max(0, (end - startedAt).TotalSeconds),
                PeakViewers = stream.PeakViewers,
                Title = stream.Title,
                Game = stream.GameName,
            },
            Followers = Of(facts, "channel.follow").Count,
            Subs = BuildSubs(facts),
            GiftSubs = BuildGifters(facts),
            Bits = BuildBits(facts),
            RaidsIn = BuildRaids(facts),
            Redemptions = await BuildRedemptionsAsync(broadcasterId, from, to, cancellationToken),
            Songs = await BuildSongsAsync(broadcasterId, from, to, cancellationToken),
            Chat = await BuildChatAsync(broadcasterId, stream.Id, cancellationToken),
        };

        return Result.Success(recap);
    }

    private async Task<Domain.Stream.Entities.Stream?> FindStreamAsync(
        Guid broadcasterId,
        string? streamId,
        CancellationToken cancellationToken
    )
    {
        IQueryable<Domain.Stream.Entities.Stream> query = db
            .Streams.AsNoTracking()
            .Where(s => s.ChannelId == broadcasterId);

        List<Domain.Stream.Entities.Stream> rows = streamId is null
            ? await query.ToListAsync(cancellationToken)
            : await query.Where(s => s.Id == streamId).ToListAsync(cancellationToken);

        // Ordered in memory: SQLite cannot order a DateTimeOffset column.
        return rows.Where(s => s.StartedAt is not null)
            .OrderByDescending(s => s.StartedAt)
            .FirstOrDefault();
    }

    private async Task<List<Fact>> LoadFactsAsync(
        Guid broadcasterId,
        DateTime from,
        DateTime to,
        CancellationToken cancellationToken
    )
    {
        // CreatedAt is the write time and never earlier than the fact, so it is a safe lower bound.
        var rows = await db
            .ChannelEvents.AsNoTracking()
            .Where(e =>
                e.ChannelId == broadcasterId && FactTypes.Contains(e.Type) && e.CreatedAt >= from
            )
            .Select(e => new { e.Type, e.Data })
            .ToListAsync(cancellationToken);

        List<Fact> facts = [];
        foreach (var row in rows)
        {
            if (JsonNode.Parse(row.Data ?? "{}") is not JsonObject data)
                continue;
            if (!TryOccurredAt(data, out DateTime at) || at < from || at > to)
                continue;
            facts.Add(new Fact(row.Type, data));
        }

        return facts;
    }

    private static bool TryOccurredAt(JsonObject data, out DateTime at) =>
        DateTime.TryParse(
            data["occurredAt"]?.GetValue<string>(),
            CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
            out at
        );

    private static List<Fact> Of(List<Fact> facts, string type) =>
        facts.Where(f => f.Type == type).ToList();

    private static int Int(JsonObject data, string key) =>
        data[key] is JsonValue v && v.TryGetValue(out int n) ? n : 0;

    private static string? Text(JsonObject data, string key) =>
        data[key] is JsonValue v && v.TryGetValue(out string? s) ? s : null;

    private static bool Flag(JsonObject data, string key) =>
        data[key] is JsonValue v && v.TryGetValue(out bool b) && b;

    private static StreamRecapSubsDto BuildSubs(List<Fact> facts)
    {
        int fresh = Of(facts, "channel.subscribe").Count(f => !Flag(f.Data, "isGift"));
        int resub = Of(facts, "channel.subscription.message").Count;
        int gift = Of(facts, "channel.subscription.gift").Sum(f => Int(f.Data, "giftCount"));
        return new()
        {
            New = fresh,
            Resub = resub,
            Gift = gift,
            Total = fresh + resub + gift,
        };
    }

    private static string NameOf(JsonObject data, string key)
    {
        bool anonymous = Flag(data, "isAnonymous");
        string? name = Text(data, key);
        return anonymous || string.IsNullOrEmpty(name) ? AnonymousName : name;
    }

    private static List<StreamRecapGifterDto> BuildGifters(List<Fact> facts) =>
        Of(facts, "channel.subscription.gift")
            .GroupBy(f => NameOf(f.Data, "gifterDisplayName"))
            .Select(g => new StreamRecapGifterDto
            {
                Name = g.Key,
                Count = g.Sum(f => Int(f.Data, "giftCount")),
            })
            .OrderByDescending(g => g.Count)
            .ThenBy(g => g.Name, StringComparer.Ordinal)
            .Take(ListCap)
            .ToList();

    private static StreamRecapBitsDto BuildBits(List<Fact> facts)
    {
        List<Fact> cheers = Of(facts, "channel.cheer");
        return new()
        {
            Total = cheers.Sum(f => Int(f.Data, "bits")),
            Top = cheers
                .GroupBy(f => NameOf(f.Data, "userDisplayName"))
                .Select(g => new StreamRecapCheererDto
                {
                    Name = g.Key,
                    Bits = g.Sum(f => Int(f.Data, "bits")),
                })
                .OrderByDescending(c => c.Bits)
                .ThenBy(c => c.Name, StringComparer.Ordinal)
                .Take(ListCap)
                .ToList(),
        };
    }

    private static List<StreamRecapRaidDto> BuildRaids(List<Fact> facts) =>
        Of(facts, "channel.raid")
            .Select(f => new StreamRecapRaidDto
            {
                Name = Text(f.Data, "fromDisplayName") ?? AnonymousName,
                Viewers = Int(f.Data, "viewerCount"),
            })
            .OrderByDescending(r => r.Viewers)
            .Take(ListCap)
            .ToList();

    private async Task<StreamRecapRedemptionsDto> BuildRedemptionsAsync(
        Guid broadcasterId,
        DateTime from,
        DateTime to,
        CancellationToken cancellationToken
    )
    {
        List<string> titles = await db
            .Redemptions.AsNoTracking()
            .Where(r =>
                r.BroadcasterId == broadcasterId && r.RedeemedAt >= from && r.RedeemedAt <= to
            )
            .Select(r => r.RewardTitle)
            .ToListAsync(cancellationToken);

        return new()
        {
            Count = titles.Count,
            Top = titles
                .GroupBy(t => t)
                .Select(g => new StreamRecapRedemptionTitleDto { Title = g.Key, Count = g.Count() })
                .OrderByDescending(t => t.Count)
                .ThenBy(t => t.Title, StringComparer.Ordinal)
                .Take(ListCap)
                .ToList(),
        };
    }

    private async Task<StreamRecapSongsDto> BuildSongsAsync(
        Guid broadcasterId,
        DateTime from,
        DateTime to,
        CancellationToken cancellationToken
    )
    {
        var rows = await db
            .Records.AsNoTracking()
            .Where(r =>
                r.BroadcasterId == broadcasterId
                && r.RecordType == SongRequestHistory.RecordType
                && r.CreatedAt >= from
                && r.CreatedAt <= to
            )
            .OrderByDescending(r => r.CreatedAt)
            .Select(r => new { r.Data })
            .ToListAsync(cancellationToken);

        List<StreamRecapSongDto> songs = [];
        foreach (var row in rows)
        {
            SongRequestHistory? track = Deserialize(row.Data);
            if (track is not null)
                songs.Add(
                    new StreamRecapSongDto { Track = track.TrackName, Artist = track.Artist }
                );
        }

        return new() { Count = songs.Count, Recent = songs.Take(ListCap).ToList() };
    }

    private static SongRequestHistory? Deserialize(string data)
    {
        try
        {
            return JsonSerializer.Deserialize<SongRequestHistory>(data, CamelCase);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private async Task<StreamRecapChatDto> BuildChatAsync(
        Guid broadcasterId,
        string streamId,
        CancellationToken cancellationToken
    )
    {
        List<string> chatters = await db
            .ChatMessages.AsNoTracking()
            .Where(m => m.BroadcasterId == broadcasterId && m.StreamId == streamId)
            .Select(m => m.UserId)
            .ToListAsync(cancellationToken);

        return new() { Messages = chatters.Count, UniqueChatters = chatters.Distinct().Count() };
    }
}
