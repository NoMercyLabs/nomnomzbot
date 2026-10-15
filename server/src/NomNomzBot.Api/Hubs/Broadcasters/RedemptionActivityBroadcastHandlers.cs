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
/// Sends an automatic channel-point reward (highlighted message, emote unlock, and so on) to the dashboard
/// as a <see cref="RedemptionActivityDto"/>, so the chat page can show it as a line.
/// </summary>
public sealed class AutomaticRewardBroadcastHandler : IEventHandler<AutomaticRewardRedeemedEvent>
{
    private readonly IDashboardNotifier _notifier;

    public AutomaticRewardBroadcastHandler(IDashboardNotifier notifier) => _notifier = notifier;

    public Task HandleAsync(AutomaticRewardRedeemedEvent @event, CancellationToken ct = default)
    {
        if (@event.BroadcasterId == Guid.Empty)
            return Task.CompletedTask;

        RedemptionActivityDto dto = new(
            BroadcasterId: @event.BroadcasterId.ToString(),
            Kind: "automatic_reward",
            RedemptionId: @event.RedemptionId,
            Title: @event.RewardType,
            UserId: @event.UserId,
            UserLogin: @event.UserLogin,
            UserDisplayName: @event.UserDisplayName,
            Cost: @event.Cost,
            CostUnit: "channel_points",
            UserInput: @event.Message,
            Timestamp: @event.OccurredAt.ToString("O")
        );

        return _notifier.SendRedemptionActivityAsync(@event.BroadcasterId.ToString(), dto, ct);
    }
}

/// <summary>
/// Sends a custom Power-up redemption (priced in Bits) to the dashboard as a
/// <see cref="RedemptionActivityDto"/>, so the chat page can show it as a line.
/// </summary>
public sealed class CustomPowerUpBroadcastHandler : IEventHandler<CustomPowerUpRedeemedEvent>
{
    private readonly IDashboardNotifier _notifier;

    public CustomPowerUpBroadcastHandler(IDashboardNotifier notifier) => _notifier = notifier;

    public Task HandleAsync(CustomPowerUpRedeemedEvent @event, CancellationToken ct = default)
    {
        if (@event.BroadcasterId == Guid.Empty)
            return Task.CompletedTask;

        RedemptionActivityDto dto = new(
            BroadcasterId: @event.BroadcasterId.ToString(),
            Kind: "custom_power_up",
            RedemptionId: @event.RedemptionId,
            Title: @event.PowerUpTitle,
            UserId: @event.UserId,
            UserLogin: @event.UserLogin,
            UserDisplayName: @event.UserDisplayName,
            Cost: @event.Bits,
            CostUnit: "bits",
            UserInput: @event.UserInput,
            Timestamp: @event.OccurredAt.ToString("O")
        );

        return _notifier.SendRedemptionActivityAsync(@event.BroadcasterId.ToString(), dto, ct);
    }
}
