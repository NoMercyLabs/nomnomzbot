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

using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Abstractions.Content;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.Dashboard.Dtos;
using NomNomzBot.Application.Dashboard.Services;
using NomNomzBot.Domain.EventStore.Entities;
using StreamEntity = NomNomzBot.Domain.Stream.Entities.Stream;

namespace NomNomzBot.Infrastructure.Stream;

/// <summary>
/// Gives a stream that lost its title the title it had, from stored facts only. In order: a twin row of the same
/// start that kept its title (the import stored some streams twice), the last <c>ChannelUpdatedEvent</c> the
/// journal saw at or before the stream's end (that title was in effect), then the title of the Twitch archive
/// recording created within <see cref="VodStartTolerance"/> of the start (Twitch keeps archives only for a limited
/// time). A stream no source knows keeps an empty title, which the recap list shows as a dash. A title that is
/// already stored is never replaced, so a re-run selects nothing new. Each filled stream that has a kept recap gets
/// that recap rebuilt, because a recap is a snapshot of the stream row. It runs after <c>StreamSplitMergeSeeder</c>
/// (945), so merged rows are settled, and before the recap backfill (950).
/// </summary>
public sealed class StreamTitleBackfillSeeder(
    IApplicationDbContext db,
    ITwitchVideosApi videos,
    IStreamRecapService recaps,
    ILogger<StreamTitleBackfillSeeder> logger
) : ISeeder
{
    public int Order => 947;

    private const string TitleChangedEventType = "ChannelUpdatedEvent";

    private static readonly TimeSpan VodStartTolerance = TimeSpan.FromMinutes(5);

    public async Task SeedAsync(CancellationToken ct = default)
    {
        List<StreamEntity> untitled = await db
            .Streams.IgnoreQueryFilters()
            .Where(s => s.StartedAt != null && (s.Title == null || s.Title == ""))
            .ToListAsync(ct);
        if (untitled.Count == 0)
            return;

        List<Guid> channelIds = untitled.Select(s => s.ChannelId).Distinct().ToList();
        Dictionary<(Guid, DateTimeOffset?), string> twins = await ReadTwinTitlesAsync(
            channelIds,
            ct
        );
        Dictionary<Guid, List<(DateTime At, string Title)>> changes = await ReadTitleChangesAsync(
            channelIds,
            ct
        );
        Dictionary<Guid, IReadOnlyList<TwitchVideo>> archives = [];

        List<StreamEntity> filled = [];
        foreach (StreamEntity row in untitled)
        {
            string? title =
                TwinTitle(twins, row)
                ?? JournalTitle(changes, row)
                ?? await VodTitleAsync(row, archives, ct);
            if (string.IsNullOrWhiteSpace(title))
                continue;

            row.Title = title;
            filled.Add(row);
        }

        if (filled.Count == 0)
            return;

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Gave {Count} stream(s) back the title they had", filled.Count);

        foreach (StreamEntity row in filled)
            await RebuildKeptRecapAsync(row, ct);
    }

    private async Task<Dictionary<(Guid, DateTimeOffset?), string>> ReadTwinTitlesAsync(
        List<Guid> channelIds,
        CancellationToken ct
    )
    {
        List<StreamEntity> titled = await db
            .Streams.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(s =>
                channelIds.Contains(s.ChannelId)
                && s.StartedAt != null
                && s.Title != null
                && s.Title != ""
            )
            .OrderBy(s => s.Id)
            .ToListAsync(ct);

        Dictionary<(Guid, DateTimeOffset?), string> twins = [];
        foreach (StreamEntity row in titled)
            twins.TryAdd((row.ChannelId, row.StartedAt), row.Title!);

        return twins;
    }

    private static string? TwinTitle(
        Dictionary<(Guid, DateTimeOffset?), string> twins,
        StreamEntity row
    ) => twins.GetValueOrDefault((row.ChannelId, row.StartedAt));

    private async Task<Dictionary<Guid, List<(DateTime At, string Title)>>> ReadTitleChangesAsync(
        List<Guid> channelIds,
        CancellationToken ct
    )
    {
        List<EventJournal> events = await db
            .EventJournals.AsNoTracking()
            .Where(j =>
                j.EventType == TitleChangedEventType
                && !j.PayloadIsEncrypted
                && j.BroadcasterId != null
                && channelIds.Contains(j.BroadcasterId.Value)
            )
            .OrderBy(j => j.OccurredAt)
            .ToListAsync(ct);

        Dictionary<Guid, List<(DateTime At, string Title)>> byChannel = [];
        foreach (EventJournal journal in events)
        {
            string? title = JsonNode.Parse(journal.Payload)?["NewTitle"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(title))
                continue;

            if (
                !byChannel.TryGetValue(
                    journal.BroadcasterId!.Value,
                    out List<(DateTime, string)>? list
                )
            )
                byChannel[journal.BroadcasterId.Value] = list = [];
            list.Add((journal.OccurredAt, title));
        }

        return byChannel;
    }

    private static string? JournalTitle(
        Dictionary<Guid, List<(DateTime At, string Title)>> changes,
        StreamEntity row
    )
    {
        if (
            row.EndedAt is not { } end
            || !changes.TryGetValue(row.ChannelId, out List<(DateTime At, string Title)>? list)
        )
            return null;

        DateTime limit = end.UtcDateTime;
        return list.LastOrDefault(c => c.At <= limit).Title;
    }

    private async Task<string?> VodTitleAsync(
        StreamEntity row,
        Dictionary<Guid, IReadOnlyList<TwitchVideo>> archives,
        CancellationToken ct
    )
    {
        if (row.StartedAt is not { } start)
            return null;

        if (!archives.TryGetValue(row.ChannelId, out IReadOnlyList<TwitchVideo>? list))
        {
            string? twitchId = await db
                .Channels.IgnoreQueryFilters()
                .Where(c => c.Id == row.ChannelId)
                .Select(c => c.TwitchChannelId)
                .FirstOrDefaultAsync(ct);
            list = string.IsNullOrEmpty(twitchId)
                ? []
                : await TwitchArchiveReader.ReadAsync(videos, twitchId, ct);
            archives[row.ChannelId] = list;
        }

        return list.FirstOrDefault(v =>
            (v.CreatedAt - start).Duration() <= VodStartTolerance
        )?.Title;
    }

    private async Task RebuildKeptRecapAsync(StreamEntity row, CancellationToken ct)
    {
        bool kept = await db
            .StreamRecaps.IgnoreQueryFilters()
            .AnyAsync(r => r.ChannelId == row.ChannelId && r.StreamId == row.Id, ct);
        if (!kept)
            return;

        Result<StreamRecapDto> rebuilt = await recaps.BuildAsync(row.ChannelId, row.Id, null, ct);
        if (rebuilt.IsFailure)
            logger.LogWarning(
                "The recap of stream {StreamId} was not rebuilt after its title came back: {Error}",
                row.Id,
                rebuilt.ErrorMessage
            );
    }
}
