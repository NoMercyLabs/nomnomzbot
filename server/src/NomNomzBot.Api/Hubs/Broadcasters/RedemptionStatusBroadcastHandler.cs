// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Api.Hubs.Dtos;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Domain.Rewards.Events;

namespace NomNomzBot.Api.Hubs.Broadcasters;

/// <summary>
/// Broadcasts a redemption's status transition (fulfilled/canceled) to dashboard clients, so the rewards page's
/// pending queue drops the row on ANY open session — not only the one that clicked fulfil/refund. Twitch echoes
/// <c>channel_points_custom_reward_redemption.update</c> over EventSub for every status change regardless of
/// who made the Helix call (the dashboard's own <c>RewardService.SetRedemptionStatusAsync</c> included), so this
/// single translator-fed event is the one true signal — same pattern as
/// <see cref="RewardLifecycleBroadcastHandler"/> for reward CRUD.
/// </summary>
public sealed class RedemptionStatusBroadcastHandler : IEventHandler<RewardRedemptionUpdatedEvent>
{
    private readonly IDashboardNotifier _notifier;

    public RedemptionStatusBroadcastHandler(IDashboardNotifier notifier) => _notifier = notifier;

    public Task HandleAsync(RewardRedemptionUpdatedEvent @event, CancellationToken ct = default)
    {
        if (@event.BroadcasterId == Guid.Empty)
            return Task.CompletedTask;

        RedemptionStatusChangedDto dto = new(
            BroadcasterId: @event.BroadcasterId.ToString(),
            RedemptionId: @event.RedemptionId,
            RewardId: @event.RewardId,
            Status: @event.Status,
            Timestamp: @event.OccurredAt.ToString("O")
        );

        return _notifier.SendRedemptionStatusChangedAsync(@event.BroadcasterId.ToString(), dto, ct);
    }
}
