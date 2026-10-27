// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Application.Music.Dtos;
using NomNomzBot.Infrastructure.Platform.Templating.Helpers.Stream;

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.Music;

/// <summary>
/// The accepted requests of the channel: the latest ones, this stream's counts and tops, and the viewer's own.
/// "This stream" means since the stream went live; offline, those tokens print as written.
/// </summary>
internal sealed class MusicHistoryHelpers()
    : MusicHelperGroup(
        MusicRow.History(
            "music.history.last",
            "Song Z by Artist Z",
            (history, _) =>
                history.Count > 0 ? MusicText.Track(history[0].TrackName, history[0].Artist) : null
        ),
        MusicRow.History(
            "music.history.list",
            "Song Z, Song Y, Song X",
            (history, call) =>
                history.Count > 0
                && MusicText.TryCount(call.Args[0], MusicText.MaxListLength, out int n)
                    ? string.Join(", ", history.Take(n).Select(item => item.TrackName))
                    : null,
            args: 1
        ),
        MusicRow.History(
            "music.history.count.stream",
            "21",
            (history, call) =>
                SinceLive(history, call) is { } stream ? StreamText.Plain(stream.Count) : null
        ),
        MusicRow.History(
            "music.history.topRequester.stream",
            "ViewerTwo (4)",
            (history, call) => TopOf(history, call, item => item.RequesterName, withCount: true)
        ),
        MusicRow.History(
            "music.history.topArtist.stream",
            "Artist A",
            (history, call) => TopOf(history, call, item => item.Artist, withCount: false)
        ),
        MusicRow
            .Custom(
                "music.user.requests.total",
                "9",
                MusicSources.Profile,
                async call =>
                    await call.Data.TotalRequestsAsync() is { } total
                        ? StreamText.Plain(total)
                        : null
            )
            .ForViewer(TemplateHelperScope.Viewer),
        MusicRow
            .History(
                "music.user.requests.stream",
                "2",
                (history, call) =>
                    SinceLive(history, call) is { } stream
                        ? StreamText.Plain(stream.Count(item => IsUser(item, call)))
                        : null
            )
            .ForViewer(TemplateHelperScope.Viewer),
        MusicRow
            .History(
                "music.user.lastRequest",
                "Song C",
                (history, call) => history.FirstOrDefault(item => IsUser(item, call))?.TrackName
            )
            .ForViewer(TemplateHelperScope.Viewer)
    )
{
    private static bool IsUser(SongHistoryItemDto item, MusicCall call) =>
        MusicText.SameName(item.RequesterName, call.Data.User);

    /// <summary>The requests made since the stream went live; null when the channel is not live.</summary>
    private static List<SongHistoryItemDto>? SinceLive(
        IReadOnlyList<SongHistoryItemDto> history,
        MusicCall call
    ) =>
        call.Scope.Channel is { IsLive: true, WentLiveAt: { } since }
            ?
            [
                .. history.Where(item =>
                    new DateTimeOffset(DateTime.SpecifyKind(item.RequestedAt, DateTimeKind.Utc))
                    >= since
                ),
            ]
            : null;

    /// <summary>
    /// The value that appears most among this stream's requests (a tie goes to the more recent one), with its
    /// count when asked.
    /// </summary>
    private static string? TopOf(
        IReadOnlyList<SongHistoryItemDto> history,
        MusicCall call,
        Func<SongHistoryItemDto, string> by,
        bool withCount
    )
    {
        if (SinceLive(history, call) is not { Count: > 0 } stream)
            return null;

        IGrouping<string, SongHistoryItemDto> top = stream
            .Where(item => !string.IsNullOrWhiteSpace(by(item)))
            .GroupBy(by, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(group => group.Count())
            .First();
        return withCount ? $"{top.First().RequesterName} ({top.Count()})" : top.Key;
    }
}
