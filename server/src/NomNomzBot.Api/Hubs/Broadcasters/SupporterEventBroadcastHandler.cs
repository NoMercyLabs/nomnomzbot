// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Domain.Supporters.Events;

namespace NomNomzBot.Api.Hubs.Broadcasters;

/// <summary>
/// Tells every open dashboard of the channel that a supporter event (tip, membership, merch, charity) was
/// recorded, so the Supporters page refetches its tips with no reload. A generic <c>supporters</c> config
/// change: the client refetches, it never patches from the payload.
/// </summary>
public sealed class SupporterEventBroadcastHandler(IDashboardNotifier notifier)
    : IEventHandler<SupporterEventReceived>
{
    public const string Domain = "supporters";

    public Task HandleAsync(SupporterEventReceived @event, CancellationToken ct = default)
    {
        if (@event.BroadcasterId == Guid.Empty)
            return Task.CompletedTask;

        string channel = @event.BroadcasterId.ToString();
        return notifier.SendConfigChangedAsync(
            channel,
            new(channel, Domain, @event.SupporterEventId.ToString(), "received"),
            ct
        );
    }
}
