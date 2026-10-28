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
