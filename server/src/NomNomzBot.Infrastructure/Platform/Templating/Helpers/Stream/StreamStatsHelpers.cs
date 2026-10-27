// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Dashboard.Dtos;

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.Stream;

/// <summary>
/// This stream's counts and rankings, from the stream stats service. Every row names the one metric it needs;
/// the provider asks the service once for the union of the metrics of a render.
/// </summary>
internal sealed class StreamStatsHelpers()
    : StreamHelperGroup(
        "stream",
        Count("stream.viewers.peak", "57", "peakViewers", s => s.PeakViewers),
        Count("stream.followers.new", "6", "followers", s => s.Followers),
        StreamHelperRow.Stats(
            "stream.followers.newList",
            "ViewerOne, ViewerTwo",
            "newFollowers",
            (s, _) => s.NewFollowers is { } names ? string.Join(", ", names) : null
        ),
        StreamHelperRow.Stats(
            "stream.followers.latest",
            "ViewerTwo",
            "newFollowers",
            (s, _) => s.NewFollowers?.LastOrDefault()
        ),
        Count("stream.subs.new", "3", "subs", s => s.Subs?.New),
        Count("stream.subs.resubs", "2", "subs", s => s.Subs?.Resub),
        Count("stream.subs.gifted", "5", "subs", s => s.Subs?.Gift),
        Count("stream.subs.total", "10", "subs", s => s.Subs?.Total),
        StreamHelperRow.Stats(
            "stream.subs.topGifter",
            "ViewerTwo",
            "gifters",
            (s, _) => s.Gifters?.FirstOrDefault()?.Name
        ),
        Count("stream.subs.selfSubs", "1", "selfSubs", s => s.SelfSubs?.Count),
        Count("stream.bits.total", "1,250", "bits", s => s.Bits?.Total),
        Ranked("stream.bits.top", "ViewerOne (500)", "bits", s => s.Bits?.Top.FirstOrDefault()),
        Count("stream.raids.in", "1", "raidsIn", s => s.RaidsIn?.Count),
        StreamHelperRow.Stats(
            "stream.raids.lastRaider",
            "OtherStreamer",
            "raidsIn",
            (s, _) => s.RaidsIn?.FirstOrDefault()?.Name
        ),
        Count(
            "stream.raids.lastRaiderViewers",
            "25",
            "raidsIn",
            s => s.RaidsIn?.FirstOrDefault()?.Viewers
        ),
        Count("stream.hypetrains", "1", "hypeTrain", s => s.HypeTrain?.Trains),
        Count("stream.hypetrains.topLevel", "3", "hypeTrain", s => s.HypeTrain?.Level),
        Count("stream.redemptions", "14", "redemptions", s => s.Redemptions?.Total),
        Count("stream.songRequesters", "4", "songRequesters", s => s.SongRequesters?.Total),
        Count("stream.messages", "2,430", "chat", s => s.Chat?.Messages),
        Count("stream.chatters.unique", "33", "chat", s => s.Chat?.UniqueChatters),
        Count("stream.commandsRun", "88", "commandsRun", s => s.CommandsRun?.Total),
        Ranked(
            "stream.topCommand",
            "!hug (31)",
            "commandsRun",
            s => s.CommandsRun?.Top.FirstOrDefault()
        ),
        Ranked(
            "stream.topChatter",
            "ViewerOne (140)",
            "topChatters",
            s => s.TopChatters?.FirstOrDefault()
        ),
        StreamHelperRow.Stats(
            "stream.topChatters",
            "ViewerOne, ViewerTwo, ViewerThree",
            "topChatters",
            (s, call) =>
                s.TopChatters is { Count: > 0 } chatters
                && StreamText.TryPositive(call.Args[0], TopMax, out int count)
                    ? string.Join(", ", chatters.Take(count).Select(c => c.Name))
                    : null,
            args: 1
        )
    )
{
    private const int TopMax = 25;

    /// <summary>A whole count of one metric, grouped by the channel language; no value prints as written.</summary>
    private static StreamHelperRow Count(
        string key,
        string sample,
        string metric,
        Func<StreamStatsDto, int?> read
    ) =>
        StreamHelperRow.Stats(
            key,
            sample,
            metric,
            (s, call) => read(s) is { } value ? StreamText.Grouped(value, call.Culture) : null
        );

    /// <summary>The first entry of a ranking as <c>Name (count)</c>.</summary>
    private static StreamHelperRow Ranked(
        string key,
        string sample,
        string metric,
        Func<StreamStatsDto, StreamStatsNamedCountDto?> read
    ) =>
        StreamHelperRow.Stats(
            key,
            sample,
            metric,
            (s, call) => read(s) is { } entry ? StreamText.Named(entry, call.Culture) : null
        );
}
