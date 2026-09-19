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
/// Forwards <see cref="MediaSharePlaybackChangedEvent"/> to the dashboard's channel group as a generic
/// <c>media_share_playback_changed</c> <c>ChannelEvent</c> (same mechanism as <see cref="SrQueueBroadcastHandler"/>
/// for the song-request queue). <c>MediaShareService</c> already moves a played/skipped/rejected item out of the
/// active lane server-side and publishes this event — the dashboard's Media-Share queue page never listened for
/// it, so a played clip only left the queue on that session's OWN next write, never on another mod's or the
/// overlay's (S-OBS-09b). The receiving client re-reads the queue for the active channel.
/// </summary>
public sealed class MediaSharePlaybackBroadcastHandler
    : IEventHandler<MediaSharePlaybackChangedEvent>
{
    private readonly IDashboardNotifier _notifier;

    public MediaSharePlaybackBroadcastHandler(IDashboardNotifier notifier) => _notifier = notifier;

    public Task HandleAsync(MediaSharePlaybackChangedEvent @event, CancellationToken ct = default)
    {
        if (@event.BroadcasterId == Guid.Empty)
            return Task.CompletedTask;

        return _notifier.NotifyChannelAsync(
            @event.BroadcasterId.ToString(),
            "media_share_playback_changed",
            new { requestId = @event.RequestId, status = @event.Status },
            ct
        );
    }
}
