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

using NomNomzBot.Domain.Federation.Events;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Api.Hubs.Broadcasters;

/// <summary>The payload of a <c>FederationOptInChanged</c> push: which choice changed and its new state.</summary>
public record FederationOptInChangedAlertDto(
    Guid? PeerId,
    string OptInType,
    string Direction,
    bool IsEnabled
);

/// <summary>
/// Pushes a federation opt-in change (on, off, new direction) to dashboard clients as
/// <c>FederationOptInChanged</c>, so the federation settings page shows a change made elsewhere with no reload.
/// </summary>
public sealed class FederationOptInChangedBroadcastHandler
    : IEventHandler<ChannelFederationOptInChangedEvent>
{
    private readonly IDashboardNotifier _notifier;

    public FederationOptInChangedBroadcastHandler(IDashboardNotifier notifier) =>
        _notifier = notifier;

    public Task HandleAsync(
        ChannelFederationOptInChangedEvent @event,
        CancellationToken ct = default
    )
    {
        if (@event.OptInBroadcasterId == Guid.Empty)
            return Task.CompletedTask;

        return _notifier.NotifyChannelAsync(
            @event.OptInBroadcasterId.ToString(),
            "FederationOptInChanged",
            new FederationOptInChangedAlertDto(
                @event.PeerId,
                @event.OptInType,
                @event.Direction,
                @event.IsEnabled
            ),
            ct
        );
    }
}
