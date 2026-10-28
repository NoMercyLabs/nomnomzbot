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
