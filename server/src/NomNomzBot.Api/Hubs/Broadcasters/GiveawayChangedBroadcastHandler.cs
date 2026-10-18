// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Domain.Giveaways.Events;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Api.Hubs.Broadcasters;

/// <summary>The payload of a <c>GiveawayChanged</c> push: which giveaway changed and how.</summary>
public record GiveawayChangedAlertDto(Guid GiveawayId, string Change);

/// <summary>
/// Pushes a giveaway change (created, edited, deleted, opened, closed, entered, drawn, redrawn) to dashboard
/// clients as <c>GiveawayChanged</c>, so the giveaways page reloads its list, entries and winners with no polling.
/// </summary>
public sealed class GiveawayChangedBroadcastHandler : IEventHandler<GiveawayChangedEvent>
{
    private readonly IDashboardNotifier _notifier;

    public GiveawayChangedBroadcastHandler(IDashboardNotifier notifier) => _notifier = notifier;

    public Task HandleAsync(GiveawayChangedEvent @event, CancellationToken ct = default)
    {
        if (@event.BroadcasterId == Guid.Empty)
            return Task.CompletedTask;

        return _notifier.NotifyChannelAsync(
            @event.BroadcasterId.ToString(),
            "GiveawayChanged",
            new GiveawayChangedAlertDto(@event.GiveawayId, @event.Change),
            ct
        );
    }
}
