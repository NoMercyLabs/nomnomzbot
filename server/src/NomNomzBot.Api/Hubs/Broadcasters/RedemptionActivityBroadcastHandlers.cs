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
/// Sends a Twitch Power-up that has no chat message of its own (the On-Screen Celebration) to the dashboard
/// as a <see cref="RedemptionActivityDto"/>. Twitch reports Power-ups only on <c>channel.bits.use</c> (the
/// automatic-reward v2 event dropped them). A Message Effect and a Gigantified Emote already show on their
/// chat message (its <c>message_type</c>), so they get no second line; a cheer is not a Power-up.
/// </summary>
public sealed class PowerUpBroadcastHandler : IEventHandler<BitsUsedEvent>
{
    private static readonly HashSet<string> ChatMessagePowerUps = new(StringComparer.Ordinal)
    {
        "message_effect",
        "gigantify_an_emote",
    };

    private readonly IDashboardNotifier _notifier;

    public PowerUpBroadcastHandler(IDashboardNotifier notifier) => _notifier = notifier;

    public Task HandleAsync(BitsUsedEvent @event, CancellationToken ct = default)
    {
        if (@event.BroadcasterId == Guid.Empty || @event.Type != "power_up")
            return Task.CompletedTask;

        string powerUp = @event.PowerUpType ?? string.Empty;
        if (ChatMessagePowerUps.Contains(powerUp))
            return Task.CompletedTask;

        RedemptionActivityDto dto = new(
            BroadcasterId: @event.BroadcasterId.ToString(),
            Kind: "power_up",
            RedemptionId: @event.EventId.ToString(),
            Title: powerUp,
            UserId: @event.UserId,
            UserLogin: @event.UserLogin,
            UserDisplayName: @event.UserDisplayName,
            Cost: @event.Bits,
            CostUnit: "bits",
            UserInput: @event.MessageText,
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
