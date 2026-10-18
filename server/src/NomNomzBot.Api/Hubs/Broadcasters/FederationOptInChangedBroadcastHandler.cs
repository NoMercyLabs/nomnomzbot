// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
