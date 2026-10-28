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

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Commands.Services;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Dashboard.Dtos;
using NomNomzBot.Application.Dashboard.Services;
using NomNomzBot.Application.Notifications.Services;
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
    private readonly IActionRequiredChangeNotifier _inbox;
    private readonly IShoutoutQueue _shoutoutQueue;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ChannelOfflineHandler> _logger;

    public ChannelOfflineHandler(
        IServiceScopeFactory scopeFactory,
        IPipelineEngine pipeline,
        IChannelRegistry registry,
        IActionRequiredChangeNotifier inbox,
        IShoutoutQueue shoutoutQueue,
        TimeProvider timeProvider,
        ILogger<ChannelOfflineHandler> logger
    )
    {
        _scopeFactory = scopeFactory;
        _pipeline = pipeline;
        _registry = registry;
        _inbox = inbox;
        _shoutoutQueue = shoutoutQueue;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    // The recap of the stream that just ended is kept now, so the gallery never waits for a raid or a restart.
    // A failed build is logged and never stops the offline flow.
    private async Task KeepRecapAsync(
        IServiceScope scope,
        Guid broadcasterId,
        string? streamId,
        CancellationToken cancellationToken
    )
    {
        IStreamRecapService? recaps = scope.ServiceProvider.GetService<IStreamRecapService>();
        if (recaps is null || streamId is null)
            return;

        try
        {
            Result<StreamRecapDto> built = await recaps.BuildAsync(
                broadcasterId,
                streamId,
                null,
                cancellationToken
            );
            if (built.IsFailure)
                _logger.LogWarning(
                    "Recap of stream {StreamId} was not kept: {Error}",
                    streamId,
                    built.ErrorMessage
                );
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Recap of stream {StreamId} failed", streamId);
        }
    }

    public async Task HandleAsync(
        ChannelOfflineEvent @event,
        CancellationToken cancellationToken = default
    )
    {
        Guid broadcasterId = @event.BroadcasterId;
        if (broadcasterId == Guid.Empty)
            return;

        // The shoutout queue lives for one stream session (old-bot parity): nothing waits across a live edge.
        await _shoutoutQueue.ClearAsync(broadcasterId, cancellationToken);

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

        ChannelContext? channelCtx = _registry.Get(broadcasterId);
        DateTimeOffset endedAt = _timeProvider.GetUtcNow();
        TimeSpan streamDuration = @event.StreamDuration;

        string? endedStreamId = channelCtx?.CurrentStreamId;

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

        // Any other row left open (an earlier missed offline, a restart) closes with it.
        await StreamRowReconciler.CloseOpenStreamsAsync(db, channel, endedAt, cancellationToken);

        if (channelCtx is not null)
        {
            channelCtx.IsLive = false;
            channelCtx.CurrentStreamId = null;
            channelCtx.WentLiveAt = null;
            channelCtx.LastStreamStartedAt = null;
        }

        channel.IsLive = false;
        await db.SaveChangesAsync(cancellationToken);
        _inbox.NotifyChanged(broadcasterId);

        _logger.LogInformation(
            "Channel {BroadcasterId} went OFFLINE after {Duration}",
            broadcasterId,
            streamDuration
        );

        await KeepRecapAsync(scope, broadcasterId, endedStreamId, cancellationToken);

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
