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

using NomNomzBot.Domain.CustomEvents.Events;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Api.Hubs.Broadcasters;

/// <summary>The payload of a <c>CustomDataSourceChanged</c> push: which source changed and how.</summary>
public record CustomDataSourceChangedAlertDto(string SourceName, string Change);

/// <summary>
/// Pushes a custom data source change (created, edited, deleted, received data) to dashboard clients as
/// <c>CustomDataSourceChanged</c>, so the custom events page reloads with no polling.
/// </summary>
public sealed class CustomDataSourceChangedBroadcastHandler
    : IEventHandler<CustomDataSourceChangedEvent>
{
    private readonly IDashboardNotifier _notifier;

    public CustomDataSourceChangedBroadcastHandler(IDashboardNotifier notifier) =>
        _notifier = notifier;

    public Task HandleAsync(CustomDataSourceChangedEvent @event, CancellationToken ct = default)
    {
        if (@event.BroadcasterId == Guid.Empty)
            return Task.CompletedTask;

        return _notifier.NotifyChannelAsync(
            @event.BroadcasterId.ToString(),
            "CustomDataSourceChanged",
            new CustomDataSourceChangedAlertDto(@event.SourceName, @event.Change),
            ct
        );
    }
}
