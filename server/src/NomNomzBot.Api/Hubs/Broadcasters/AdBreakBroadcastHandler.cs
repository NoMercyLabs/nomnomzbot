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
