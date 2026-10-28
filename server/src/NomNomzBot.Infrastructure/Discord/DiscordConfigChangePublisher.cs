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

using NomNomzBot.Domain.Platform.Events;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Infrastructure.Discord;

/// <summary>
/// Tells every open dashboard of the channel that a Discord config row (live role or notification rule)
/// changed, so the Discord page refetches instead of going stale.
/// </summary>
internal static class DiscordConfigChangePublisher
{
    public static Task PublishAsync(
        IEventBus eventBus,
        Guid broadcasterId,
        Guid rowId,
        string action,
        CancellationToken ct
    ) =>
        eventBus.PublishAsync(
            new ChannelConfigChangedEvent
            {
                BroadcasterId = broadcasterId,
                Domain = "discord",
                EntityId = rowId.ToString(),
                Action = action,
            },
            ct
        );
}
