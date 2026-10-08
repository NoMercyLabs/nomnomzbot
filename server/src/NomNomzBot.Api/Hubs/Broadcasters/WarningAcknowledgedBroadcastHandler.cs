// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
