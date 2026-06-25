// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Api.Hubs.Dtos;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Domain.Rewards.Events;

namespace NomNomzBot.Api.Hubs.Broadcasters;

/// <summary>Broadcasts cheer/bits alerts to dashboard/overlay clients.</summary>
public sealed class CheerBroadcastHandler : IEventHandler<CheerEvent>
{
    private readonly IDashboardNotifier _notifier;

    public CheerBroadcastHandler(IDashboardNotifier notifier) => _notifier = notifier;

    public Task HandleAsync(CheerEvent @event, CancellationToken ct = default)
    {
        if (@event.BroadcasterId == Guid.Empty)
            return Task.CompletedTask;

        return _notifier.NotifyChannelAsync(
            @event.BroadcasterId.ToString(),
            "cheer",
            new CheerAlertDto(
                @event.IsAnonymous ? null : @event.UserId,
                @event.IsAnonymous ? "Anonymous" : @event.UserDisplayName,
                @event.Bits,
                @event.Message,
                @event.IsAnonymous
            ),
            ct
        );
    }
}
