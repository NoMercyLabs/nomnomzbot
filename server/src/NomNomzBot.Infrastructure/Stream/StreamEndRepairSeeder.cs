// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Abstractions.Content;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Contracts.Twitch;

namespace NomNomzBot.Infrastructure.Stream;

/// <summary>
/// Repairs Stream rows closed by the old online handler, which stamped a missed-offline row with the NEXT
/// stream's <c>StartedAt</c>. Such a row claims the whole gap between two streams as live time. The repair
/// finds every row whose <c>EndedAt</c> equals the next row's <c>StartedAt</c> for the same channel and moves
/// the end to the last proof the stream was live (<see cref="StreamRowReconciler.LastActivityAsync"/>: the
/// latest chat or journaled viewer-count sample inside its window), else to the row's own start. A real
/// observed end is never equal to the next start, so a correct row is never selected. Idempotent: a repaired
/// row no longer matches, so a re-run selects nothing. A row stamped any other way is also repaired when its end
/// lies more than an hour past its last proof of live time; a stream's length never decides. The analytics duration is derived from
/// <c>EndedAt - StartedAt</c> on read, so repairing <c>EndedAt</c> repairs the duration. Plain EF, so it runs
/// the same on SQLite and Postgres.
/// </summary>
public sealed class StreamEndRepairSeeder : ISeeder
{
    private readonly IApplicationDbContext _db;
    private readonly ILogger<StreamEndRepairSeeder> _logger;

    private readonly ITwitchVideosApi _videos;

    public StreamEndRepairSeeder(
        IApplicationDbContext db,
        ITwitchVideosApi videos,
        ILogger<StreamEndRepairSeeder> logger
    )
    {
        _db = db;
        _videos = videos;
        _logger = logger;
    }

    // A stored end this far past the last proof of live time is a stale end. Length never decides: a 14 h or a
    // 24 h stream with proof up to its end stays as it is.
    private static readonly TimeSpan StaleEndSlack = TimeSpan.FromHours(1);

    private static readonly TimeSpan ChatOnlySlack = TimeSpan.FromHours(6);

    // Late, like the other backfills: it only reads chat, the journal and the Streams rows themselves.
    public int Order => 940;

    // Only a row longer than this asks Twitch for its recording: a long row is where a stale end hides.
    private static readonly TimeSpan VodCheckLength = TimeSpan.FromHours(12);

    // A recording belongs to a row when it was created within this of the row's start.
    private static readonly TimeSpan VodStartTolerance = TimeSpan.FromMinutes(5);

    private static readonly Regex VodDuration = new(
        @"^(?:(?<h>\d+)h)?(?:(?<m>\d+)m)?(?:(?<s>\d+)s)?$",
        RegexOptions.Compiled
    );

    /// <summary>
    /// The end of the Twitch archive recording that belongs to the row (created within five minutes of the
    /// row's start): its creation time plus its duration. Null when the row is not long, the channel has no
    /// Twitch id, Twitch cannot answer, or no recording matches (Twitch keeps archives only for a limited time).
    /// Archives are read once per channel.
    /// </summary>
    private async Task<DateTimeOffset?> VodEndAsync(
        Domain.Stream.Entities.Stream row,
        Dictionary<Guid, IReadOnlyList<TwitchVideo>> archives,
        CancellationToken ct
    )
    {
        if (row.StartedAt is not { } start || row.EndedAt - start <= VodCheckLength)
            return null;

        if (!archives.TryGetValue(row.ChannelId, out IReadOnlyList<TwitchVideo>? list))
        {
            string? twitchId = await _db
                .Channels.IgnoreQueryFilters()
                .Where(c => c.Id == row.ChannelId)
                .Select(c => c.TwitchChannelId)
                .FirstOrDefaultAsync(ct);
            list = string.IsNullOrEmpty(twitchId)
                ? []
                : await TwitchArchiveReader.ReadAsync(_videos, twitchId, ct);
            archives[row.ChannelId] = list;
        }

        TwitchVideo? vod = list.FirstOrDefault(v =>
            (v.CreatedAt - start).Duration() <= VodStartTolerance
        );
        if (vod is null)
            return null;

        Match match = VodDuration.Match(vod.Duration);
        if (!match.Success || vod.Duration.Length == 0)
            return null;

        TimeSpan length = new(ParsePart(match, "h"), ParsePart(match, "m"), ParsePart(match, "s"));
        return vod.CreatedAt + length;
    }

    private static int ParsePart(Match match, string name) =>
        match.Groups[name].Success ? int.Parse(match.Groups[name].Value) : 0;

    public async Task SeedAsync(CancellationToken ct = default)
    {
        List<Domain.Stream.Entities.Stream> rows = await _db
            .Streams.IgnoreQueryFilters()
            .Where(s => s.StartedAt != null)
            .OrderBy(s => s.ChannelId)
            .ThenBy(s => s.StartedAt)
            .ToListAsync(ct);

        Dictionary<Guid, IReadOnlyList<TwitchVideo>> archives = [];
        int repaired = 0;
        for (int i = 0; i < rows.Count; i++)
        {
            Domain.Stream.Entities.Stream row = rows[i];
            if (row.EndedAt is null)
                continue;

            // Twitch's own recording of the stream is the strongest proof of its end.
            DateTimeOffset? vodEnd = await VodEndAsync(row, archives, ct);
            if (vodEnd is { } recorded && row.EndedAt - recorded > StaleEndSlack)
            {
                row.EndedAt = recorded;
                repaired++;
                continue;
            }

            DateTimeOffset? nextStart = rows.Skip(i + 1)
                .TakeWhile(r => r.ChannelId == row.ChannelId)
                .Select(r => r.StartedAt)
                .FirstOrDefault(s => s > row.StartedAt);

            // Window of the proof: up to the end the row claims, but never past the next stream's start.
            DateTimeOffset windowEnd =
                nextStart is { } next && next < row.EndedAt ? next : row.EndedAt.Value;
            bool stampedWithNextStart = row.EndedAt == nextStart;

            // Viewer samples are the proof. Where none exist (before sampling began) the chat of the first run
            // stands in for it, with a wide slack because chat can go quiet well before a real stream ends.
            // The window end is exclusive: a sample stamped at the row's own end still counts, so a repaired
            // row (end = its last sample) is stable on a second run.
            DateTimeOffset? sample = await StreamRowReconciler.LastSampleAsync(
                _db,
                row.ChannelId,
                row,
                !stampedWithNextStart && windowEnd == row.EndedAt
                    ? windowEnd.AddTicks(1)
                    : windowEnd,
                ct
            );
            DateTimeOffset? lastActivity =
                sample
                ?? await StreamRowReconciler.LastActivityAsync(
                    _db,
                    row.ChannelId,
                    row,
                    windowEnd,
                    ct
                );
            TimeSpan slack = sample is null ? ChatOnlySlack : StaleEndSlack;

            // A row with no proof at all is only touched when it was stamped with the next start.
            if (lastActivity is null && !stampedWithNextStart)
                continue;

            DateTimeOffset proven = lastActivity ?? row.StartedAt!.Value;

            // A stream whose proof reaches (nearly) its stored end is real, whatever its length: leave it alone.
            if (!stampedWithNextStart && row.EndedAt - proven <= slack)
                continue;

            row.EndedAt = proven;
            repaired++;
        }

        if (repaired == 0)
            return;

        await _db.SaveChangesAsync(ct);
        _logger.LogWarning(
            "Repaired {Count} stream row(s) whose end was stamped with the next stream's start",
            repaired
        );
    }
}
