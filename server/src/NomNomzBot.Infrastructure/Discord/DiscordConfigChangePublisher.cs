// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
