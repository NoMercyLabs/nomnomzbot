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
using NomNomzBot.Application.Moderation.Services;
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
    private readonly IHeldTextService? _heldText;

    public AutomaticRewardBroadcastHandler(
        IDashboardNotifier notifier,
        IHeldTextService? heldText = null
    )
    {
        _notifier = notifier;
        _heldText = heldText;
    }

    public async Task HandleAsync(
        AutomaticRewardRedeemedEvent @event,
        CancellationToken ct = default
    )
    {
        if (@event.BroadcasterId == Guid.Empty)
            return;

        string? message = await RedemptionActivityText.ReleasedAsync(
            _heldText,
            @event.BroadcasterId,
            @event.UserId,
            @event.Message,
            @event.OccurredAt,
            ct
        );

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
            UserInput: message,
            Timestamp: @event.OccurredAt.ToString("O")
        );

        await _notifier.SendRedemptionActivityAsync(@event.BroadcasterId.ToString(), dto, ct);
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
    private readonly IHeldTextService? _heldText;

    public PowerUpBroadcastHandler(IDashboardNotifier notifier, IHeldTextService? heldText = null)
    {
        _notifier = notifier;
        _heldText = heldText;
    }

    public async Task HandleAsync(BitsUsedEvent @event, CancellationToken ct = default)
    {
        if (@event.BroadcasterId == Guid.Empty || @event.Type != "power_up")
            return;

        string powerUp = @event.PowerUpType ?? string.Empty;
        if (ChatMessagePowerUps.Contains(powerUp))
            return;

        string? message = await RedemptionActivityText.ReleasedAsync(
            _heldText,
            @event.BroadcasterId,
            @event.UserId,
            @event.MessageText,
            @event.OccurredAt,
            ct
        );

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
            UserInput: message,
            Timestamp: @event.OccurredAt.ToString("O")
        );

        await _notifier.SendRedemptionActivityAsync(@event.BroadcasterId.ToString(), dto, ct);
    }
}

/// <summary>
/// Sends a custom Power-up redemption (priced in Bits) to the dashboard as a
/// <see cref="RedemptionActivityDto"/>, so the chat page can show it as a line.
/// </summary>
public sealed class CustomPowerUpBroadcastHandler : IEventHandler<CustomPowerUpRedeemedEvent>
{
    private readonly IDashboardNotifier _notifier;
    private readonly IHeldTextService? _heldText;

    public CustomPowerUpBroadcastHandler(
        IDashboardNotifier notifier,
        IHeldTextService? heldText = null
    )
    {
        _notifier = notifier;
        _heldText = heldText;
    }

    public async Task HandleAsync(CustomPowerUpRedeemedEvent @event, CancellationToken ct = default)
    {
        if (@event.BroadcasterId == Guid.Empty)
            return;

        string? input = await RedemptionActivityText.ReleasedAsync(
            _heldText,
            @event.BroadcasterId,
            @event.UserId,
            @event.UserInput,
            @event.OccurredAt,
            ct
        );

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
            UserInput: input,
            Timestamp: @event.OccurredAt.ToString("O")
        );

        await _notifier.SendRedemptionActivityAsync(@event.BroadcasterId.ToString(), dto, ct);
    }
}

internal static class RedemptionActivityText
{
    public static async Task<string?> ReleasedAsync(
        IHeldTextService? heldText,
        Guid broadcasterId,
        string userId,
        string? text,
        DateTimeOffset occurredAt,
        CancellationToken ct
    ) =>
        heldText is not null
        && !string.IsNullOrWhiteSpace(text)
        && await heldText.IsTextHeldAsync(broadcasterId, userId, text, ct, occurredAt)
            ? null
            : text;
}
