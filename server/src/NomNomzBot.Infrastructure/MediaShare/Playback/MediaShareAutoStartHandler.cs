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

using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.MediaShare.Dtos;
using NomNomzBot.Application.MediaShare.Services;
using NomNomzBot.Application.Widgets.Dtos;
using NomNomzBot.Application.Widgets.Services;
using NomNomzBot.Domain.MediaShare.Entities;
using NomNomzBot.Domain.MediaShare.Events;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Infrastructure.MediaShare.Playback;

/// <summary>
/// Starts playback by itself: when an item becomes approved, nothing plays, and the media_share overlay is
/// attached, the player gets a play command. Without an overlay the item stays queued, because a command
/// sent to no listener would mark the item playing while the stream shows nothing.
/// </summary>
public sealed class MediaShareAutoStartHandler(
    IMediaShareService queue,
    IMediaSharePlaybackService playback,
    IWidgetService widgets,
    IOverlayPresenceRegistry presence
) : IEventHandler<MediaSharePlaybackChangedEvent>
{
    private const string MediaShareSurfaceKey = "media_share";

    public async Task HandleAsync(
        MediaSharePlaybackChangedEvent @event,
        CancellationToken ct = default
    )
    {
        if (@event.Status != MediaShareStatus.Approved || @event.BroadcasterId == Guid.Empty)
            return;

        if (await IsPlayingAsync(@event.BroadcasterId, ct))
            return;

        if (!await IsOverlayAttachedAsync(@event.BroadcasterId, ct))
            return;

        await playback.SendCommandAsync(
            @event.BroadcasterId,
            new(MediaPlaybackCommands.Play, null, null, null),
            ct
        );
    }

    private async Task<bool> IsPlayingAsync(Guid broadcasterId, CancellationToken ct)
    {
        Result<PagedList<MediaShareRequestDto>> playing = await queue.GetQueueAsync(
            broadcasterId,
            new(MediaShareStatus.Playing),
            new(1, 1),
            ct
        );
        return playing.IsSuccess && playing.Value.Items.Count > 0;
    }

    private async Task<bool> IsOverlayAttachedAsync(Guid broadcasterId, CancellationToken ct)
    {
        Result<WidgetDetail> surface = await widgets.EnsureSystemWidgetAsync(
            broadcasterId.ToString(),
            MediaShareSurfaceKey,
            ct
        );
        return surface is { IsSuccess: true, Value.IsEnabled: true }
            && presence.IsWidgetAttached(broadcasterId, surface.Value.Id);
    }
}
