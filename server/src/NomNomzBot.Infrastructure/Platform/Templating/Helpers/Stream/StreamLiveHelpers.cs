// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.Stream;

/// <summary>
/// The running stream, read from the channel's live in-memory state: uptime, start time, status, title,
/// category and viewer count. Uptime, start time, id and viewers exist only while the channel is live.
/// </summary>
internal sealed class StreamLiveHelpers()
    : StreamHelperGroup(
        "stream",
        StreamHelperRow.Memory(
            "stream.uptime.long",
            "2 hours, 15 minutes",
            (_, call) => call.Uptime is { } up ? StreamText.Words(up, call.Culture) : null
        ),
        StreamHelperRow.Memory(
            "stream.uptime.clock",
            "2:15:00",
            (_, call) => call.Uptime is { } up ? StreamText.Clock(up) : null
        ),
        StreamHelperRow.Memory(
            "stream.uptime.minutes",
            "135",
            (_, call) => call.Uptime is { } up ? StreamText.Plain((long)up.TotalMinutes) : null
        ),
        StreamHelperRow.Memory(
            "stream.uptime.hours",
            "2.25",
            (_, call) => call.Uptime is { } up ? StreamText.Hours(up) : null
        ),
        StreamHelperRow.Memory(
            "stream.uptime.since",
            "live since 13:15",
            (channel, call) =>
                StartedAt(channel, call) is { } start
                    ? "live since " + StreamText.LocalTime(start, channel.Timezone)
                    : null
        ),
        StreamHelperRow.Memory(
            "stream.startedAt.local",
            "13:15",
            (channel, call) =>
                StartedAt(channel, call) is { } start
                    ? StreamText.LocalTime(start, channel.Timezone)
                    : null
        ),
        StreamHelperRow.Memory(
            "stream.startedAt.relative",
            "2 hours ago",
            (channel, call) =>
                StartedAt(channel, call) is { } start
                    ? StreamText.Ago(start, call.Scope.Now, call.Culture)
                    : null
        ),
        StreamHelperRow.Memory(
            "stream.status",
            "live",
            (channel, _) => channel.IsLive ? "live" : "offline"
        ),
        StreamHelperRow.Memory(
            "stream.id",
            "40123456789",
            (channel, _) =>
                channel.IsLive && !string.IsNullOrEmpty(channel.CurrentStreamId)
                    ? channel.CurrentStreamId
                    : null
        ),
        StreamHelperRow.Memory(
            "stream.title.short",
            "Building…",
            (channel, call) =>
                !string.IsNullOrWhiteSpace(channel.CurrentTitle)
                && StreamText.TryPositive(call.Args[0], TitleMax, out int max)
                    ? StreamText.Cut(channel.CurrentTitle, max)
                    : null,
            args: 1
        ),
        StreamHelperRow.Memory(
            "stream.game.short",
            "Software and Game Development",
            (channel, _) =>
                !string.IsNullOrWhiteSpace(channel.CurrentGame)
                    ? StreamText.Cut(channel.CurrentGame, GameMax)
                    : null
        ),
        StreamHelperRow.Memory(
            "stream.game.link",
            "https://twitch.tv/directory/category/software-and-game-development",
            (channel, _) =>
                StreamText.Slug(channel.CurrentGame ?? string.Empty) is { Length: > 0 } slug
                    ? "https://twitch.tv/directory/category/" + slug
                    : null
        ),
        StreamHelperRow.Memory(
            "stream.viewers.compact",
            "42",
            (channel, _) => channel.IsLive ? StreamText.Compact(channel.ViewerCount) : null
        )
    )
{
    private const int TitleMax = 140;
    private const int GameMax = 40;

    private static DateTimeOffset? StartedAt(ChannelContext channel, StreamHelperCall call) =>
        call.Uptime is null ? null : channel.WentLiveAt;
}
