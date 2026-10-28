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
