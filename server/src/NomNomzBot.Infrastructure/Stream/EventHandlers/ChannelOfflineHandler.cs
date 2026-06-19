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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Domain.Platform.Entities;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Platform;
using NomNomzBot.Domain.Stream.Events;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Application.Abstractions.Pipeline;

namespace NomNomzBot.Infrastructure.Stream.EventHandlers;

/// <summary>
/// Updates Channel.IsLive = false and cancels all running pipelines when
/// the stream goes offline via EventSub stream.offline.
/// Computes actual stream duration from ChannelContext.WentLiveAt.
/// </summary>
public sealed class ChannelOfflineHandler : IEventHandler<ChannelOfflineEvent>
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IPipelineEngine _pipeline;
    private readonly IChannelRegistry _registry;
    private readonly ILogger<ChannelOfflineHandler> _logger;

    public ChannelOfflineHandler(
        IServiceScopeFactory scopeFactory,
        IPipelineEngine pipeline,
        IChannelRegistry registry,
        ILogger<ChannelOfflineHandler> logger
    )
    {
        _scopeFactory = scopeFactory;
        _pipeline = pipeline;
        _registry = registry;
        _logger = logger;
    }

    public async Task HandleAsync(
        ChannelOfflineEvent @event,
        CancellationToken cancellationToken = default
    )
    {
        string? broadcasterId = @event.BroadcasterId;
        if (string.IsNullOrEmpty(broadcasterId))
            return;

        using IServiceScope scope = _scopeFactory.CreateScope();
        IApplicationDbContext db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

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
        DateTimeOffset endedAt = DateTimeOffset.UtcNow;
        TimeSpan streamDuration =
            channelCtx?.WentLiveAt.HasValue == true
                ? endedAt - channelCtx.WentLiveAt.Value
                : @event.StreamDuration;

        // Finalize the Stream record with EndedAt
        if (channelCtx?.CurrentStreamId is not null)
        {
            global::NomNomzBot.Domain.Stream.Entities.Stream? streamRecord = await db.Streams.FindAsync(
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

        await ExecuteEventResponseAsync(
            db,
            broadcasterId,
            "stream_offline",
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["broadcaster"] = @event.BroadcasterDisplayName,
                ["duration"] = streamDuration.ToString(@"hh\:mm\:ss"),
            },
            cancellationToken
        );
    }

    private async Task ExecuteEventResponseAsync(
        IApplicationDbContext db,
        string broadcasterId,
        string eventType,
        Dictionary<string, string> variables,
        CancellationToken ct
    )
    {
        Record? config = await db.Records.FirstOrDefaultAsync(
            r => r.BroadcasterId == broadcasterId && r.RecordType == $"event_response:{eventType}",
            ct
        );

        if (config is null || string.IsNullOrWhiteSpace(config.Data))
            return;

        try
        {
            await _pipeline.ExecuteAsync(
                new()
                {
                    BroadcasterId = broadcasterId,
                    PipelineJson = config.Data,
                    TriggeredByUserId = broadcasterId,
                    TriggeredByDisplayName = string.Empty,
                    RawMessage = string.Empty,
                    InitialVariables = variables,
                },
                ct
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to execute event_response pipeline for {EventType} in {Channel}",
                eventType,
                broadcasterId
            );
        }
    }
}
