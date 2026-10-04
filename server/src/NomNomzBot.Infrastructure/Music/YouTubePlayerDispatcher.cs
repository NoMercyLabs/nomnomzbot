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
    /// <summary>
    /// The one player widget that owns the channel's YouTube audio, as a list of zero or one id. Candidates are
    /// the enabled widgets that subscribe <c>now_playing</c> or <c>youtube.play</c> and have a page open. The
    /// owner is the candidate whose report the store accepted last, else the earliest created.
    /// </summary>
    Task<IReadOnlyList<Guid>> FindPlayersAsync(
        Guid broadcasterId,
        CancellationToken cancellationToken = default
    );

    /// <summary>Sends <c>youtube.play</c> for the video to the given player widgets.</summary>
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
    IWidgetEventNotifier notifier,
    IYouTubePlayerStateStore store
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

        List<Widget> attached =
        [
            .. widgets
                .Where(w =>
                    w.EventSubscriptions.Contains(NowPlayingEventType)
                    || w.EventSubscriptions.Contains(PlayEventType)
                )
                .Where(w => presence.IsWidgetAttached(broadcasterId, w.Id)),
        ];
        if (attached.Count == 0)
            return [];

        Guid? reporting = store.ReportingWidget(broadcasterId);
        Widget owner =
            attached.FirstOrDefault(w => w.Id == reporting)
            ?? attached.OrderBy(w => w.CreatedAt).ThenBy(w => w.Id).First();
        return [owner.Id];
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
