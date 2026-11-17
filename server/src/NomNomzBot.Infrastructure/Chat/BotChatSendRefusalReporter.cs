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

using System.Collections.Concurrent;
using NomNomzBot.Application.Chat.Services;
using NomNomzBot.Domain.Chat.Events;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Infrastructure.Chat;

/// <summary>
/// Publishes <see cref="BotChatSendRefusedEvent"/> for a refused bot send, at most once per channel per
/// <see cref="Throttle"/>, and <see cref="BotChatSendRecoveredEvent"/> on the first success after it. The state
/// is one entry per channel in memory: a restart forgets it, and then the inbox item ends on its own window.
/// </summary>
public sealed class BotChatSendRefusalReporter(IEventBus eventBus, TimeProvider clock)
    : IBotChatSendRefusalReporter
{
    /// <summary>The shortest time between two refusal notices for one channel.</summary>
    public static readonly TimeSpan Throttle = TimeSpan.FromHours(1);

    private readonly ConcurrentDictionary<Guid, DateTimeOffset> _lastRefusalByChannel = new();

    public async Task ReportRefusedAsync(
        Guid channelId,
        string botUsername,
        CancellationToken cancellationToken = default
    )
    {
        DateTimeOffset now = clock.GetUtcNow();
        bool publish = false;
        _lastRefusalByChannel.AddOrUpdate(
            channelId,
            _ =>
            {
                publish = true;
                return now;
            },
            (_, last) =>
            {
                publish = now - last >= Throttle;
                return publish ? now : last;
            }
        );
        if (!publish)
            return;

        await eventBus.PublishAsync(
            new BotChatSendRefusedEvent
            {
                BroadcasterId = channelId,
                BotUsername = botUsername,
                OccurredAt = now,
            },
            cancellationToken
        );
    }

    public async Task ReportDeliveredAsync(
        Guid channelId,
        CancellationToken cancellationToken = default
    )
    {
        if (!_lastRefusalByChannel.TryRemove(channelId, out _))
            return;

        await eventBus.PublishAsync(
            new BotChatSendRecoveredEvent
            {
                BroadcasterId = channelId,
                OccurredAt = clock.GetUtcNow(),
            },
            cancellationToken
        );
    }
}
