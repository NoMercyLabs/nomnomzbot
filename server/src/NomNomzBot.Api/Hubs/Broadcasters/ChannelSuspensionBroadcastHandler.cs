// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Identity.Events;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Api.Hubs.Broadcasters;

/// <summary>
/// Tells every dashboard user of a channel that the platform suspended it, so the dashboard closes its frame
/// and shows the suspended view live.
/// </summary>
public sealed class ChannelSuspendedBroadcastHandler : IEventHandler<ChannelSuspendedEvent>
{
    private readonly IDashboardNotifier _notifier;
    private readonly IChannelAccessService _access;

    public ChannelSuspendedBroadcastHandler(
        IDashboardNotifier notifier,
        IChannelAccessService access
    )
    {
        _notifier = notifier;
        _access = access;
    }

    public async Task HandleAsync(ChannelSuspendedEvent @event, CancellationToken ct = default)
    {
        IReadOnlyList<string> users = await _access.ListDashboardUserIdsAsync(
            @event.BroadcasterId,
            ct
        );
        string channel = @event.BroadcasterId.ToString();

        await _notifier.SendChannelSuspensionChangedAsync(
            channel,
            users,
            new(channel, @event.Status, @event.Reason, @event.OccurredAt.ToString("O")),
            ct
        );
    }
}

/// <summary>
/// Tells every dashboard user of a channel that the platform reinstated it, so the dashboard opens its frame
/// again without a reload.
/// </summary>
public sealed class ChannelReinstatedBroadcastHandler : IEventHandler<ChannelReinstatedEvent>
{
    private readonly IDashboardNotifier _notifier;
    private readonly IChannelAccessService _access;

    public ChannelReinstatedBroadcastHandler(
        IDashboardNotifier notifier,
        IChannelAccessService access
    )
    {
        _notifier = notifier;
        _access = access;
    }

    public async Task HandleAsync(ChannelReinstatedEvent @event, CancellationToken ct = default)
    {
        IReadOnlyList<string> users = await _access.ListDashboardUserIdsAsync(
            @event.BroadcasterId,
            ct
        );
        string channel = @event.BroadcasterId.ToString();

        await _notifier.SendChannelSuspensionChangedAsync(
            channel,
            users,
            new(channel, AuthEnums.ChannelStatus.Active, null, @event.OccurredAt.ToString("O")),
            ct
        );
    }
}
