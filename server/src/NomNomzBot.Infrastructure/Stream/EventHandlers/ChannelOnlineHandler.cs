// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Commands.Services;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Domain.Stream.Events;

namespace NomNomzBot.Infrastructure.Stream.EventHandlers;

/// <summary>
/// Updates Channel.IsLive = true, refreshes title/game, and creates a Stream record
/// when a stream comes online via EventSub stream.online.
/// Also resets per-session ChannelContext state (chatters, shoutout cooldowns) and runs the
/// operator's configured <c>stream.online</c> event response through the shared executor.
/// stream.online EventSub does not include title/game — these are fetched from Helix.
/// </summary>
public sealed class ChannelOnlineHandler : IEventHandler<ChannelOnlineEvent>
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IChannelRegistry _registry;
    private readonly ILogger<ChannelOnlineHandler> _logger;

    public ChannelOnlineHandler(
        IServiceScopeFactory scopeFactory,
        IChannelRegistry registry,
        ILogger<ChannelOnlineHandler> logger
    )
    {
        _scopeFactory = scopeFactory;
        _registry = registry;
        _logger = logger;
    }

    public async Task HandleAsync(
        ChannelOnlineEvent @event,
        CancellationToken cancellationToken = default
    )
    {
        Guid broadcasterId = @event.BroadcasterId;
        if (broadcasterId == Guid.Empty)
            return;

        using IServiceScope scope = _scopeFactory.CreateScope();
        IApplicationDbContext db =
            scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

        Channel? channel = await db.Channels.FindAsync([broadcasterId], cancellationToken);
        if (channel is null)
        {
            _logger.LogWarning(
                "ChannelOnlineEvent received for unknown channel {BroadcasterId}",
                broadcasterId
            );
            return;
        }

        // stream.online EventSub payload has no title/game — fetch from Helix if empty
        string title = @event.StreamTitle;
        string gameName = @event.GameName;
        if (string.IsNullOrEmpty(title))
        {
            ITwitchStreamsApi streams =
                scope.ServiceProvider.GetRequiredService<ITwitchStreamsApi>();
            Result<TwitchStream> streamResult = await streams.GetStreamAsync(
                broadcasterId,
                cancellationToken
            );
            if (streamResult.IsSuccess)
            {
                title = streamResult.Value.Title;
                gameName = streamResult.Value.GameName;
            }
        }

        channel.IsLive = true;
        if (!string.IsNullOrEmpty(title))
            channel.Title = title;
        if (!string.IsNullOrEmpty(gameName))
            channel.GameName = gameName;

        string? streamId = Ulid.NewUlid().ToString();
        Domain.Stream.Entities.Stream stream = new()
        {
            Id = streamId,
            ChannelId = broadcasterId,
            Title = title,
            GameName = gameName,
            StartedAt = @event.StartedAt,
        };
        db.Streams.Add(stream);

        await db.SaveChangesAsync(cancellationToken);

        // Reset per-session in-memory state and populate live tracking.
        // GetOrCreateAsync ensures the channel is in the registry even after an eviction window.
        ChannelContext channelCtx = await _registry.GetOrCreateAsync(
            broadcasterId,
            channel.TwitchChannelId ?? broadcasterId.ToString(),
            channel.Name,
            cancellationToken
        );
        channelCtx.IsLive = true;
        channelCtx.CurrentStreamId = streamId;
        channelCtx.WentLiveAt = @event.StartedAt;
        channelCtx.CurrentTitle = title;
        channelCtx.CurrentGame = gameName;
        channelCtx.SessionChatters.Clear();
        channelCtx.LastShoutoutPerUser.Clear();
        channelCtx.LastGlobalShoutout = null;

        _logger.LogInformation(
            "Channel {BroadcasterId} is now LIVE: {Title} playing {Game}",
            broadcasterId,
            @event.StreamTitle,
            @event.GameName
        );

        // The operator's configured "stream.online" response (the row the event-responses page edits) —
        // through the shared executor, like every other trigger source.
        IEventResponseExecutor executor =
            scope.ServiceProvider.GetRequiredService<IEventResponseExecutor>();
        await executor.ExecuteAsync(
            broadcasterId,
            "stream.online",
            userId: null,
            userDisplayName: @event.BroadcasterDisplayName,
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["broadcaster"] = @event.BroadcasterDisplayName,
                ["title"] = title,
                ["game"] = gameName,
            },
            cancellationToken
        );
    }
}
