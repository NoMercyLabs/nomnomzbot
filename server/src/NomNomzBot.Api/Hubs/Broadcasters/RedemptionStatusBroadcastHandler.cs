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
