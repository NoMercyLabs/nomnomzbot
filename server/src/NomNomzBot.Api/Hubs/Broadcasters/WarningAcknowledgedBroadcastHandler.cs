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

using NomNomzBot.Domain.Moderation.Events;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Api.Hubs.Broadcasters;

/// <summary>
/// Tells every open dashboard of the channel that a warned viewer acknowledged their warning
/// (<c>channel.warning.acknowledge</c>), so the mod log and the viewer card refetch and show "acknowledged".
/// A generic <c>moderation-history</c> config change — the client refetches, it never patches from the payload.
/// </summary>
public sealed class WarningAcknowledgedBroadcastHandler(IDashboardNotifier notifier)
    : IEventHandler<WarningAcknowledgedEvent>
{
    public const string Domain = "moderation-history";

    public Task HandleAsync(WarningAcknowledgedEvent @event, CancellationToken ct = default)
    {
        if (@event.BroadcasterId == Guid.Empty)
            return Task.CompletedTask;

        string channel = @event.BroadcasterId.ToString();
        return notifier.SendConfigChangedAsync(
            channel,
            new(channel, Domain, @event.UserId, "warning_acknowledged"),
            ct
        );
    }
}
