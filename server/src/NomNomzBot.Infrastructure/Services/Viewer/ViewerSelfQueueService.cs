// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.EntityFrameworkCore;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Viewer;
using NomNomzBot.Application.Music.Services;

namespace NomNomzBot.Infrastructure.Services.Viewer;

/// <summary>
/// Reads the live queue and keeps only the caller's own requests. A queue item names its requester by the
/// name chat saw, so it matches the caller's display name or login, case-insensitively.
/// </summary>
public sealed class ViewerSelfQueueService(IApplicationDbContext db, IMusicService music)
    : IViewerSelfQueueService
{
    public async Task<Result<ViewerQueueDto>> GetMyQueueAsync(
        Guid channelId,
        Guid callerUserId,
        CancellationToken ct = default
    )
    {
        var caller = await db
            .Users.Where(u => u.Id == callerUserId)
            .Select(u => new { u.DisplayName, u.Username })
            .FirstOrDefaultAsync(ct);
        if (caller is null)
            return Result.Failure<ViewerQueueDto>("User not found.", "NOT_FOUND");

        MusicQueue queue = await music.GetQueueAsync(channelId.ToString(), ct);
        List<ViewerQueuedSongDto> mine = [];
        for (int index = 0; index < queue.Queue.Count; index++)
        {
            MusicQueueItem item = queue.Queue[index];
            if (IsCaller(item.RequestedBy, caller.DisplayName, caller.Username))
                mine.Add(
                    new(
                        index + 1,
                        item.TrackName,
                        item.Artist,
                        item.ImageUrl,
                        item.DurationMs,
                        item.Code
                    )
                );
        }

        return Result.Success(new ViewerQueueDto(queue.Queue.Count, mine));
    }

    private static bool IsCaller(string? requestedBy, string? displayName, string username) =>
        requestedBy is not null
        && (
            string.Equals(requestedBy, displayName, StringComparison.OrdinalIgnoreCase)
            || string.Equals(requestedBy, username, StringComparison.OrdinalIgnoreCase)
        );
}
