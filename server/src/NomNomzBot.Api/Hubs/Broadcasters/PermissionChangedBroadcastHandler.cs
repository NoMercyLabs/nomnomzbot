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

using NomNomzBot.Domain.Identity.Events;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Api.Hubs.Broadcasters;

/// <summary>Broadcasts permission changes to dashboard clients.</summary>
public sealed class PermissionChangedBroadcastHandler : IEventHandler<PermissionChangedEvent>
{
    private readonly IDashboardNotifier _notifier;

    public PermissionChangedBroadcastHandler(IDashboardNotifier notifier) => _notifier = notifier;

    public Task HandleAsync(PermissionChangedEvent @event, CancellationToken ct = default)
    {
        if (@event.BroadcasterId == Guid.Empty)
            return Task.CompletedTask;

        return _notifier.SendPermissionChangedAsync(
            @event.BroadcasterId.ToString(),
            new(
                @event.SubjectType,
                @event.SubjectId,
                @event.ResourceType,
                @event.ResourceId,
                @event.NewPermissionValue
            ),
            ct
        );
    }
}
