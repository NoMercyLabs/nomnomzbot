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
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Commands.Services;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Domain.Stream.Events;

namespace NomNomzBot.Infrastructure.Stream.EventHandlers;

/// <summary>
/// Updates Channel.IsLive = false and cancels all running pipelines when
/// the stream goes offline via EventSub stream.offline.
/// Computes actual stream duration from ChannelContext.WentLiveAt and runs the operator's
/// configured <c>stream.offline</c> event response through the shared executor.
/// </summary>
public sealed class ChannelOfflineHandler : IEventHandler<ChannelOfflineEvent>
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IPipelineEngine _pipeline;
    private readonly IChannelRegistry _registry;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ChannelOfflineHandler> _logger;

    public ChannelOfflineHandler(
        IServiceScopeFactory scopeFactory,
        IPipelineEngine pipeline,
        IChannelRegistry registry,
        TimeProvider timeProvider,
        ILogger<ChannelOfflineHandler> logger
    )
    {
        _scopeFactory = scopeFactory;
        _pipeline = pipeline;
        _registry = registry;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task HandleAsync(
        ChannelOfflineEvent @event,
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
                "ChannelOfflineEvent received for unknown channel {BroadcasterId}",
                broadcasterId
            );
            return;
        }

        // Compute actual stream duration from ChannelContext before resetting state
        ChannelContext? channelCtx = _registry.Get(broadcasterId);
        DateTimeOffset endedAt = _timeProvider.GetUtcNow();
        TimeSpan streamDuration =
            channelCtx?.WentLiveAt.HasValue == true
                ? endedAt - channelCtx.WentLiveAt.Value
                : @event.StreamDuration;

        // Finalize the Stream record with EndedAt
        if (channelCtx?.CurrentStreamId is not null)
        {
            Domain.Stream.Entities.Stream? streamRecord = await db.Streams.FindAsync(
                [channelCtx.CurrentStreamId],
                cancellationToken
            );
            if (streamRecord is not null)
                streamRecord.EndedAt = endedAt;
        }

        if (channelCtx is not null)
        {
            channelCtx.IsLive = false;
            channelCtx.CurrentStreamId = null;
            channelCtx.WentLiveAt = null;
        }

        channel.IsLive = false;
        await db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Channel {BroadcasterId} went OFFLINE after {Duration}",
            broadcasterId,
            streamDuration
        );

        await _pipeline.CancelAllForChannelAsync(broadcasterId);

        // The operator's configured "stream.offline" response (the row the event-responses page edits) —
        // through the shared executor, like every other trigger source.
        IEventResponseExecutor executor =
            scope.ServiceProvider.GetRequiredService<IEventResponseExecutor>();
        await executor.ExecuteAsync(
            broadcasterId,
            "stream.offline",
            userId: null,
            userDisplayName: @event.BroadcasterDisplayName,
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["broadcaster"] = @event.BroadcasterDisplayName,
                ["duration"] = streamDuration.ToString(@"hh\:mm\:ss"),
            },
            cancellationToken
        );
    }
}
