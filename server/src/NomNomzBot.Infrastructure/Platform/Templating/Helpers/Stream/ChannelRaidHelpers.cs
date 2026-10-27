// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.Raids;

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.Stream;

/// <summary>
/// The channel's raid history with other channels (<c>IRaidHistoryReader</c>): how many raids went out to or
/// came in from a channel, and who and when the latest ones were. An unknown login has had 0 raids.
/// </summary>
internal sealed class ChannelRaidHelpers()
    : StreamHelperGroup(
        "channel",
        Raids(
            "channel.raids.outCount",
            "3",
            async (history, call) => await CountAsync(history.Outgoing, call),
            args: 1
        ),
        Raids(
            "channel.raids.inCount",
            "5",
            async (history, call) => await CountAsync(history.Incoming, call),
            args: 1
        ),
        Raids(
            "channel.raids.lastOut",
            "OtherStreamer",
            async (history, call) =>
                Latest(history.Outgoing) is { } id
                    ? (await call.Data.UserByIdAsync(id, call.Ct))?.DisplayName
                    : null
        ),
        Raids(
            "channel.raids.lastIn.at",
            "2026-10-24",
            (history, _) =>
                Task.FromResult<string?>(
                    Latest(history.Incoming) is { } id
                        ? StreamText.Date(history.Incoming[id].LastAt)
                        : null
                )
        )
    )
{
    private static StreamHelperRow Raids(
        string key,
        string sample,
        Func<RaidHistory, StreamHelperCall, Task<string?>> render,
        int args = 0
    ) =>
        StreamHelperRow.ReadAsync(
            key,
            sample,
            StreamSources.Raids,
            call => call.Data.RaidHistoryAsync(call.Ct),
            render,
            args
        );

    private static async Task<string?> CountAsync(
        IReadOnlyDictionary<string, RaidStats> side,
        StreamHelperCall call
    )
    {
        string login = call.Args[0].TrimStart('@');
        if (login.Length == 0)
            return null;

        TwitchUser? user = await call.Data.UserByLoginAsync(login, call.Ct);
        return StreamText.Plain(
            user is not null && side.TryGetValue(user.Id, out RaidStats? stats) ? stats.Count : 0
        );
    }

    /// <summary>The user id of the most recent raid on one side of the history.</summary>
    private static string? Latest(IReadOnlyDictionary<string, RaidStats> side) =>
        side.Count == 0 ? null : side.MaxBy(pair => pair.Value.LastAt).Key;
}
