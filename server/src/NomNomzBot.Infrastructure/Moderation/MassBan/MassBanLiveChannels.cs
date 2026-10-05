// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;

namespace NomNomzBot.Infrastructure.Moderation.MassBan;

/// <summary>
/// Which of a set of channels are live right now, asked of Twitch (Get Streams by user id, 100 per call). Twitch is
/// asked rather than the local channel row because a moderated channel need not be served by the bot at all.
/// </summary>
public sealed class MassBanLiveChannels
{
    private const int MaxIdsPerCall = 100;

    private readonly ITwitchStreamsApi _streams;

    public MassBanLiveChannels(ITwitchStreamsApi streams)
    {
        _streams = streams;
    }

    /// <summary>The Twitch ids among <paramref name="channelTwitchIds"/> that are live. Any failed call fails the whole answer.</summary>
    public async Task<Result<HashSet<string>>> FindLiveAsync(
        IReadOnlyList<string> channelTwitchIds,
        CancellationToken ct
    )
    {
        HashSet<string> live = new(StringComparer.Ordinal);
        foreach (string[] chunk in channelTwitchIds.Distinct().Chunk(MaxIdsPerCall))
        {
            Result<TwitchPage<TwitchStream>> page = await _streams.GetStreamsAsync(
                new(UserIds: chunk),
                new(PageSize: MaxIdsPerCall),
                ct
            );
            if (page.IsFailure)
                return page.WithValue<HashSet<string>>(default!);

            foreach (TwitchStream stream in page.Value.Items)
                live.Add(stream.UserId);
        }

        return Result.Success(live);
    }
}
