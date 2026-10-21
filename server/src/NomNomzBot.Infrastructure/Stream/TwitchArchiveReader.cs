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

namespace NomNomzBot.Infrastructure.Stream;

/// <summary>
/// Reads a channel's Twitch archive recordings (past broadcasts), newest first, for the seeders that repair a
/// stream row from its recording. Twitch keeps archives only for a limited time, so an old stream has none.
/// </summary>
internal static class TwitchArchiveReader
{
    private const int MaxPages = 10;

    /// <summary>
    /// Every archive Twitch still holds for the channel, up to <see cref="MaxPages"/> pages. A failed page ends the
    /// read with what was gathered, so the caller never fails because Twitch could not answer.
    /// </summary>
    public static async Task<IReadOnlyList<TwitchVideo>> ReadAsync(
        ITwitchVideosApi videos,
        string twitchId,
        CancellationToken ct
    )
    {
        List<TwitchVideo> all = [];
        string? after = null;
        for (int page = 0; page < MaxPages; page++)
        {
            Result<TwitchPage<TwitchVideo>> result = await videos.GetVideosByTwitchUserIdAsync(
                twitchId,
                "archive",
                null,
                "time",
                new TwitchPageRequest(after),
                ct
            );
            if (result.IsFailure)
                break;

            all.AddRange(result.Value.Items);
            if (result.Value.NextCursor is null)
                break;
            after = result.Value.NextCursor;
        }

        return all;
    }
}
