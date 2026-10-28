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

using NomNomzBot.Application.Music.Services;
using NomNomzBot.Infrastructure.Platform.Templating.Helpers.People;
using NomNomzBot.Infrastructure.Platform.Templating.Helpers.Stream;

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.Music;

/// <summary>The song-request queue as a whole: its size, length, next songs, people and cost.</summary>
internal sealed class MusicQueueHelpers()
    : MusicHelperGroup(
        MusicRow.Queue("music.queue.length", "5", (queue, _) => StreamText.Plain(queue.Count)),
        MusicRow.Queue(
            "music.queue.duration",
            "17m 20s",
            (queue, _) =>
                queue.Count == 0
                    ? null
                    : MusicText.Short(queue.Sum(item => (long)item.DurationMs) / 1000)
        ),
        MusicRow.Queue(
            "music.queue.next",
            "Song B by Artist B",
            (queue, _) => queue.Count > 0 ? Described(queue[0]) : null
        ),
        MusicRow.Queue(
            "music.queue.next.requester",
            "ViewerThree",
            (queue, _) =>
                queue.Count > 0 && !string.IsNullOrWhiteSpace(queue[0].RequestedBy)
                    ? queue[0].RequestedBy
                    : null
        ),
        MusicRow.Queue(
            "music.queue.item",
            "Song C by Artist C",
            (queue, call) =>
                MusicText.TryCount(call.Args[0], queue.Count, out int place)
                    ? Described(queue[place - 1])
                    : null,
            args: 1
        ),
        MusicRow.Queue(
            "music.queue.list",
            "1. Song B, 2. Song C, 3. Song D",
            (queue, call) =>
                queue.Count > 0
                && MusicText.TryCount(call.Args[0], MusicText.MaxListLength, out int n)
                    ? string.Join(
                        ", ",
                        queue.Take(n).Select((item, index) => $"{index + 1}. {item.TrackName}")
                    )
                    : null,
            args: 1
        ),
        MusicRow.Queue(
            "music.queue.requesters",
            "4",
            (queue, _) =>
                StreamText.Plain(
                    queue
                        .Select(item => item.RequestedBy)
                        .Where(who => !string.IsNullOrWhiteSpace(who))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .Count()
                )
        ),
        MusicRow.Queue(
            "music.queue.isEmpty",
            "false",
            (queue, _) => PeopleText.Flag(queue.Count == 0)
        ),
        MusicRow.Queue(
            "music.queue.cost.total",
            "1,500",
            (queue, call) => StreamText.Grouped(queue.Sum(item => (long)item.Cost), call.Culture)
        )
    )
{
    private static string? Described(MusicQueueItem item) =>
        MusicText.Track(item.TrackName, item.Artist);
}
