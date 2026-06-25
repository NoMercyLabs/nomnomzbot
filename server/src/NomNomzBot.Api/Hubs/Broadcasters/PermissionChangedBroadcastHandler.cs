// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
