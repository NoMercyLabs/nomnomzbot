// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Api.Hubs.Dtos;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Domain.MediaShare.Events;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Api.Hubs.Broadcasters;

/// <summary>
/// A playback command the server issues (<see cref="MediaPlayerCommandIssuedEvent"/>) → the channel's
/// media-share overlay widget as a <c>media_share_command</c> widget event. The push goes through the shared
/// subscription-matched dispatch, so only this channel's widgets that declare <c>media_share_command</c>
/// receive it. A command is not a feed item: nothing is captured for replay.
/// </summary>
public sealed class MediaPlaybackOverlayBroadcastHandler(
    IApplicationDbContext db,
    IWidgetNotifier widgets
) : IEventHandler<MediaPlayerCommandIssuedEvent>
{
    public Task HandleAsync(
        MediaPlayerCommandIssuedEvent @event,
        CancellationToken cancellationToken = default
    )
    {
        MediaShareCommandWidgetPayload payload = new(
            @event.Command,
            @event.RequestId,
            @event.SourceType,
            @event.MediaRef,
            @event.SourceUrl,
            @event.Title,
            @event.DurationSeconds,
            @event.PositionSeconds,
            @event.Volume
        );

        return WidgetAlertDispatch.PushToSubscribersAsync(
            db,
            widgets,
            @event.BroadcasterId,
            "media_share_command",
            payload,
            cancellationToken
        );
    }
}
