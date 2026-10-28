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
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Dashboard.Dtos;
using NomNomzBot.Application.Dashboard.Stats;

namespace NomNomzBot.Infrastructure.Dashboard.Stats;

/// <summary>
/// The highest viewer count Twitch reported while the stream was live: the larger of the peak kept on the stream
/// row and the largest journaled viewer-count sample inside the window. The row's peak alone under-reports when
/// the poll missed a stretch, so the samples are read too. Omitted when neither exists.
/// </summary>
public sealed class PeakViewersMetric(IApplicationDbContext db) : StreamMetric<int?>
{
    private const string ViewerSampleEventType = "StreamViewerCountSampledEvent";

    public override string Key => "peakViewers";

    protected override async Task<int?> ComputeAsync(StreamWindow window, CancellationToken ct)
    {
        DateTime from = window.From;
        DateTime to = window.To;
        List<string> payloads = await db
            .EventJournals.AsNoTracking()
            .Where(j =>
                j.BroadcasterId == window.ChannelId
                && j.EventType == ViewerSampleEventType
                && j.OccurredAt >= from
                && j.OccurredAt <= to
            )
            .Select(j => j.Payload)
            .ToListAsync(ct);

        int? sampled = payloads.Select(ViewerCountOf).Max();
        return window.PeakViewers is { } stored && stored > (sampled ?? -1) ? stored : sampled;
    }

    private static int? ViewerCountOf(string payload) =>
        JsonNode.Parse(payload)?["ViewerCount"] is JsonValue v && v.TryGetValue(out int count)
            ? count
            : null;

    protected override void Assign(StreamStatsDto stats, int? result) => stats.PeakViewers = result;
}
