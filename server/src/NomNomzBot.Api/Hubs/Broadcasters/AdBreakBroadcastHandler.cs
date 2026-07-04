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
using NomNomzBot.Domain.Stream.Events;

namespace NomNomzBot.Api.Hubs.Broadcasters;

/// <summary>Broadcasts a starting mid-roll ad break (<c>channel.ad_break.begin</c>) to dashboard clients.</summary>
public sealed class AdBreakBeganBroadcastHandler : IEventHandler<AdBreakBeganEvent>
{
    private readonly IDashboardNotifier _notifier;

    public AdBreakBeganBroadcastHandler(IDashboardNotifier notifier) => _notifier = notifier;

    public Task HandleAsync(AdBreakBeganEvent @event, CancellationToken ct = default)
    {
        if (@event.BroadcasterId == Guid.Empty)
            return Task.CompletedTask;

        return _notifier.NotifyChannelAsync(
            @event.BroadcasterId.ToString(),
            "ad_break_begin",
            new AdBreakBeganAlertDto(
                @event.DurationSeconds,
                @event.IsAutomatic,
                @event.StartedAt,
                @event.RequesterUserId,
                @event.RequesterDisplayName
            ),
            ct
        );
    }
}
