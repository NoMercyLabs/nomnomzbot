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
using NomNomzBot.Application.Widgets.Services;
using NomNomzBot.Domain.Widgets.Entities;

namespace NomNomzBot.Infrastructure.Music;

/// <summary>Finds the channel's open YouTube player pages and tells them what to play.</summary>
public interface IYouTubePlayerDispatcher
{
    /// <summary>Ids of the enabled widgets that subscribe <c>now_playing</c> or <c>youtube.play</c> and have a page open.</summary>
    Task<IReadOnlyList<Guid>> FindPlayersAsync(
        Guid broadcasterId,
        CancellationToken cancellationToken = default
    );

    /// <summary>Sends <c>youtube.play</c> for the video to every given player widget.</summary>
    Task PlayAsync(
        Guid broadcasterId,
        IReadOnlyList<Guid> players,
        string videoId,
        CancellationToken cancellationToken = default
    );
}

public sealed class YouTubePlayerDispatcher(
    IApplicationDbContext db,
    IOverlayPresenceRegistry presence,
    IWidgetEventNotifier notifier
) : IYouTubePlayerDispatcher
{
    public const string PlayEventType = "youtube.play";
    private const string NowPlayingEventType = "now_playing";

    public async Task<IReadOnlyList<Guid>> FindPlayersAsync(
        Guid broadcasterId,
        CancellationToken cancellationToken = default
    )
    {
        List<Widget> widgets = await db
            .Widgets.AsNoTracking()
            .Where(w => w.BroadcasterId == broadcasterId && w.IsEnabled)
            .ToListAsync(cancellationToken);

        return
        [
            .. widgets
                .Where(w =>
                    w.EventSubscriptions.Contains(NowPlayingEventType)
                    || w.EventSubscriptions.Contains(PlayEventType)
                )
                .Where(w => presence.IsWidgetAttached(broadcasterId, w.Id))
                .Select(w => w.Id),
        ];
    }

    public async Task PlayAsync(
        Guid broadcasterId,
        IReadOnlyList<Guid> players,
        string videoId,
        CancellationToken cancellationToken = default
    )
    {
        YouTubePlayWidgetPayload payload = new(
            videoId,
            $"https://www.youtube.com/watch?v={videoId}"
        );
        foreach (Guid widgetId in players)
            await notifier.SendWidgetEventAsync(
                broadcasterId,
                widgetId,
                PlayEventType,
                payload,
                cancellationToken
            );
    }
}
