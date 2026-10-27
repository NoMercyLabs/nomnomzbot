// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Contracts.Analytics;

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.Stream;

/// <summary>
/// The last finished stream: the first ended stream of the channel's newest few. A channel that never finished
/// a stream has none, and each row prints as written.
/// </summary>
internal sealed class StreamLastHelpers()
    : StreamHelperGroup(
        "stream",
        Last(
            "stream.last.endedAt",
            "2026-10-24",
            (s, _) => s.EndedAt is { } ended ? StreamText.Date(ended) : null
        ),
        Last(
            "stream.last.since",
            "1 day, 16 hours",
            (s, call) =>
                s.EndedAt is { } ended
                    ? StreamText.Words(call.Scope.Now - ended, call.Culture)
                    : null
        ),
        Last(
            "stream.last.relative",
            "2 days ago",
            (s, call) =>
                s.EndedAt is { } ended ? StreamText.Ago(ended, call.Scope.Now, call.Culture) : null
        ),
        Last(
            "stream.last.duration",
            "4h 10m",
            (s, _) =>
                s.DurationSeconds is { } seconds
                    ? StreamText.Short(TimeSpan.FromSeconds(seconds))
                    : null
        ),
        Last("stream.last.game", "Just Chatting", (s, _) => NotBlank(s.GameName)),
        Last("stream.last.title", "Chill Saturday", (s, _) => NotBlank(s.Title)),
        Last(
            "stream.last.peak",
            "61",
            (s, call) => s.PeakViewers is { } peak ? StreamText.Grouped(peak, call.Culture) : null
        ),
        Last(
            "stream.last.followers",
            "9",
            (s, call) => StreamText.Grouped(s.NewFollowers, call.Culture)
        ),
        Last(
            "stream.last.messages",
            "3,102",
            (s, call) => StreamText.Grouped(s.TotalMessages, call.Culture)
        ),
        StreamHelperRow.ReadAsync(
            "stream.last.subs",
            "4",
            StreamSources.Recent,
            call => call.Data.LastStreamDetailAsync(call.Ct),
            (detail, call) =>
                Task.FromResult<string?>(StreamText.Grouped(detail.NewSubscribers, call.Culture))
        )
    )
{
    private static string? NotBlank(string? text) => string.IsNullOrWhiteSpace(text) ? null : text;

    private static StreamHelperRow Last(
        string key,
        string sample,
        Func<StreamListItemDto, StreamHelperCall, string?> render
    ) => StreamHelperRow.Read(key, sample, StreamSources.Recent, LastEndedAsync, render);

    private static async Task<StreamListItemDto?> LastEndedAsync(StreamHelperCall call) =>
        (await call.Data.RecentAsync(call.Ct))?.FirstOrDefault(s => s.EndedAt is not null);
}
