// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot. You may use, change and share it for free,
//  for any noncommercial purpose, under the PolyForm Noncommercial License
//  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
//  See LICENSE for details, and TRADEMARKS.md for the name and logo.
//
//  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
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
