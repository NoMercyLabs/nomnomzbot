// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Domain.Stream.Events;
using NomNomzBot.Infrastructure.Chat.Jobs;

namespace NomNomzBot.Infrastructure.Chat.EventHandlers;

/// <summary>
/// Warms a channel's third-party emote sets the moment it goes live (chat-decoration spec §3.6), so a viewer's first
/// message after the stream starts already renders the channel's emotes instead of waiting for the periodic 5-min sweep
/// in <see cref="ChatDecorationRefreshService"/>. Best-effort: it resolves the channel's Twitch id/login from the
/// registry and delegates to the warmer; an unknown channel is a no-op, and a provider failure keeps the last-good cache.
/// </summary>
public sealed class StreamWentLiveEmoteWarmer : IEventHandler<ChannelOnlineEvent>
{
    private readonly IChannelRegistry _channels;
    private readonly ChatEmoteCacheWarmer _warmer;

    public StreamWentLiveEmoteWarmer(IChannelRegistry channels, ChatEmoteCacheWarmer warmer)
    {
        _channels = channels;
        _warmer = warmer;
    }

    public async Task HandleAsync(
        ChannelOnlineEvent @event,
        CancellationToken cancellationToken = default
    )
    {
        ChannelContext? channel = _channels.Get(@event.BroadcasterId);
        if (channel is null)
            return;

        await _warmer.WarmChannelAsync(
            channel.TwitchChannelId,
            channel.ChannelName,
            cancellationToken
        );
    }
}
