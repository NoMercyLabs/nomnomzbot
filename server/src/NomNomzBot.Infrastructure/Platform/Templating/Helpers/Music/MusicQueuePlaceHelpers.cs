// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Humanizer;
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Domain.Music.Events;
using NomNomzBot.Infrastructure.Platform.Templating.Helpers.Stream;

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.Music;

/// <summary>
/// One person's place in the queue: the viewer who typed the command (<c>mine</c>, place and wait) and the
/// first mention (<c>target</c>). These read the numbered queue snapshot, matched by display name.
/// </summary>
internal sealed class MusicQueuePlaceHelpers()
    : MusicHelperGroup(
        MusicRow
            .Snapshot(
                "music.queue.position",
                "3",
                (queue, call) =>
                    FirstOf(queue, call.Data.User) is { } mine
                        ? StreamText.Plain(mine.Number)
                        : null
            )
            .ForViewer(TemplateHelperScope.Viewer),
        MusicRow
            .Snapshot(
                "music.queue.position.ordinal",
                "3rd",
                (queue, call) =>
                    FirstOf(queue, call.Data.User) is { } mine
                        ? mine.Number.Ordinalize(call.Culture)
                        : null
            )
            .ForViewer(TemplateHelperScope.Viewer),
        MusicRow
            .Custom(
                "music.queue.wait",
                "7m 40s",
                MusicSources.Snapshot | MusicSources.NowPlaying,
                async call =>
                    await WaitSecondsAsync(call) is { } seconds ? MusicText.Short(seconds) : null
            )
            .ForViewer(TemplateHelperScope.Viewer),
        MusicRow
            .Custom(
                "music.queue.wait.at",
                "about 15:38",
                MusicSources.Snapshot | MusicSources.NowPlaying,
                async call =>
                    await WaitSecondsAsync(call) is { } seconds
                        ? "about "
                            + StreamText.LocalTime(
                                call.Now.AddSeconds(seconds + RoundToMinute),
                                call.Timezone
                            )
                        : null
            )
            .ForViewer(TemplateHelperScope.Viewer),
        MusicRow
            .Snapshot(
                "music.queue.mine.count",
                "2",
                (queue, call) => StreamText.Plain(Of(queue, call.Data.User).Count())
            )
            .ForViewer(TemplateHelperScope.Viewer),
        MusicRow
            .Snapshot(
                "music.queue.mine.titles",
                "Song C, Song E",
                (queue, call) =>
                    Of(queue, call.Data.User).Select(item => item.Title).ToList()
                        is { Count: > 0 } titles
                        ? string.Join(", ", titles)
                        : null
            )
            .ForViewer(TemplateHelperScope.Viewer),
        MusicRow
            .Snapshot(
                "music.queue.mine.code",
                "K7QM",
                (queue, call) =>
                    FirstOf(queue, call.Data.User) is { Code.Length: > 0 } mine ? mine.Code : null
            )
            .ForViewer(TemplateHelperScope.Viewer),
        MusicRow
            .Snapshot(
                "music.queue.mine.next",
                "Song C",
                (queue, call) => FirstOf(queue, call.Data.User)?.Title
            )
            .ForViewer(TemplateHelperScope.Viewer),
        MusicRow
            .Snapshot(
                "music.queue.target.count",
                "1",
                (queue, call) =>
                    call.Data.Target.Length > 0
                        ? StreamText.Plain(Of(queue, call.Data.Target).Count())
                        : null
            )
            .ForViewer(TemplateHelperScope.Viewer)
    )
{
    /// <summary>Half a minute: added before the clock text drops the seconds, it rounds to the nearest minute.</summary>
    private const int RoundToMinute = 30;

    private static IEnumerable<SongRequestQueueSnapshotItem> Of(
        IReadOnlyList<SongRequestQueueSnapshotItem> queue,
        string who
    ) => queue.Where(item => MusicText.SameName(item.RequestedBy, who));

    private static SongRequestQueueSnapshotItem? FirstOf(
        IReadOnlyList<SongRequestQueueSnapshotItem> queue,
        string who
    ) => Of(queue, who).FirstOrDefault();

    /// <summary>
    /// How long until the person's next song starts: what is left of the playing track plus every song queued
    /// before it. Null when the person has no song in the queue.
    /// </summary>
    private static async Task<long?> WaitSecondsAsync(MusicCall call)
    {
        IReadOnlyList<SongRequestQueueSnapshotItem> queue = call.Data.Snapshot();
        int mine = queue
            .Select((item, index) => (item, index))
            .Where(pair => MusicText.SameName(pair.item.RequestedBy, call.Data.User))
            .Select(pair => pair.index)
            .DefaultIfEmpty(-1)
            .First();
        if (mine < 0)
            return null;

        long before = queue.Take(mine).Sum(item => (long)item.DurationSec);
        long left = await call.Data.NowPlayingAsync() is { } now
            ? Math.Max(0, now.DurationMs - now.ProgressMs) / 1000
            : 0;
        return before + left;
    }
}
