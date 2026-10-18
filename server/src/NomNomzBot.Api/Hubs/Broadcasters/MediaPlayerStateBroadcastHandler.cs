// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Domain.MediaShare.Events;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Api.Hubs.Broadcasters;

/// <summary>
/// What the media-share player reported (<see cref="MediaPlayerStateChangedEvent"/>) → the owning channel's
/// dashboard group as <c>media_share_player_state</c>, so the dashboard player card follows the stream.
/// </summary>
public sealed class MediaPlayerStateBroadcastHandler(IDashboardNotifier dashboard)
    : IEventHandler<MediaPlayerStateChangedEvent>
{
    public Task HandleAsync(
        MediaPlayerStateChangedEvent @event,
        CancellationToken cancellationToken = default
    )
    {
        if (@event.BroadcasterId == Guid.Empty)
            return Task.CompletedTask;

        return dashboard.NotifyChannelAsync(
            @event.BroadcasterId.ToString(),
            "media_share_player_state",
            new
            {
                state = @event.State,
                requestId = @event.RequestId,
                title = @event.Title,
                sharerName = @event.SharerName,
                thumbnailUrl = @event.ThumbnailUrl,
                positionSeconds = @event.PositionSeconds,
                durationSeconds = @event.DurationSeconds,
                volume = @event.Volume,
            },
            cancellationToken
        );
    }
}
