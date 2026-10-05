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
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Commands.Services;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Domain.Stream.Events;
using NomNomzBot.Infrastructure.Platform.Eventing;

namespace NomNomzBot.Infrastructure.Stream.AdBreak;

/// <summary>
/// Old-bot parity (MonetizationEventHandler): after an ad break the chat hears that it ended. The old bot slept
/// for the break's duration inside the event handler; this keeps the pending breaks in memory and a worker ticks
/// <see cref="ProcessDueAsync"/>, so no handler is held. The end line runs through the
/// <c>channel.ad_break.end</c> event response, so it follows the channel's tone and switch. A break belongs to
/// one stream (<c>WentLiveAt</c>): when the channel went offline or live again during the wait, nothing is said.
/// </summary>
public sealed class AdBreakEndScheduler : IAdBreakEndScheduler
{
    internal const string EndResponseKey = "channel.ad_break.end";

    private readonly Lock _gate = new();
    private readonly List<PendingBreak> _pending = [];

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IChannelRegistry _registry;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<AdBreakEndScheduler> _logger;

    public AdBreakEndScheduler(
        IServiceScopeFactory scopeFactory,
        IChannelRegistry registry,
        TimeProvider timeProvider,
        ILogger<AdBreakEndScheduler> logger
    )
    {
        _scopeFactory = scopeFactory;
        _registry = registry;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public void Schedule(Guid broadcasterId, DateTimeOffset startedAt, int durationSeconds)
    {
        ChannelContext? channel = _registry.Get(broadcasterId);
        if (channel is not { IsLive: true, WentLiveAt: DateTimeOffset streamStart })
            return;

        PendingBreak item = new(
            broadcasterId,
            startedAt.AddSeconds(durationSeconds),
            streamStart,
            durationSeconds
        );
        lock (_gate)
            _pending.Add(item);
    }

    public async Task ProcessDueAsync(CancellationToken cancellationToken)
    {
        DateTimeOffset now = _timeProvider.GetUtcNow();
        List<PendingBreak> due;
        lock (_gate)
        {
            due = [.. _pending.Where(p => p.DueAt <= now)];
            _pending.RemoveAll(p => p.DueAt <= now);
        }

        foreach (PendingBreak item in due)
        {
            try
            {
                await SayEndAsync(item, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Ad break end line for channel {Channel} failed",
                    item.BroadcasterId
                );
            }
        }
    }

    private async Task SayEndAsync(PendingBreak item, CancellationToken cancellationToken)
    {
        ChannelContext? channel = _registry.Get(item.BroadcasterId);
        if (channel is not { IsLive: true } || channel.WentLiveAt != item.StreamStart)
            return;

        using IServiceScope scope = _scopeFactory.CreateScope();
        IEventResponseExecutor executor =
            scope.ServiceProvider.GetRequiredService<IEventResponseExecutor>();
        await executor.ExecuteAsync(
            item.BroadcasterId,
            EndResponseKey,
            null,
            null,
            new()
            {
                ["ad.duration"] = TwitchAlertHandlerBase<AdBreakBeganEvent>.HumanDuration(
                    item.DurationSeconds
                ),
            },
            cancellationToken
        );
    }

    private sealed record PendingBreak(
        Guid BroadcasterId,
        DateTimeOffset DueAt,
        DateTimeOffset StreamStart,
        int DurationSeconds
    );
}
