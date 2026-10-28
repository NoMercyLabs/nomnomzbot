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
/// Broadcasts channel point reward CONFIG lifecycle (create/update/remove on Twitch) to dashboard clients, so
/// the rewards page live-updates. Distinct from <see cref="RewardRedeemedBroadcastHandler"/>, which broadcasts a
/// redemption. <see cref="NomNomzBot.Infrastructure.Rewards.EventHandlers.RewardLifecycleHandler"/> is the
/// sibling read-model handler that keeps the local <c>Reward</c> row in sync with the same events.
/// </summary>
public sealed class RewardLifecycleBroadcastHandler
    : IEventHandler<RewardCreatedEvent>,
        IEventHandler<RewardUpdatedEvent>,
        IEventHandler<RewardRemovedEvent>
{
    private readonly IDashboardNotifier _notifier;

    public RewardLifecycleBroadcastHandler(IDashboardNotifier notifier) => _notifier = notifier;

    public Task HandleAsync(RewardCreatedEvent @event, CancellationToken ct = default) =>
        BroadcastAsync(
            @event.BroadcasterId,
            "created",
            @event.TwitchRewardId,
            @event.Title,
            @event.Cost,
            @event.IsEnabled,
            @event.OccurredAt,
            ct
        );

    public Task HandleAsync(RewardUpdatedEvent @event, CancellationToken ct = default) =>
        BroadcastAsync(
            @event.BroadcasterId,
            "updated",
            @event.TwitchRewardId,
            @event.Title,
            @event.Cost,
            @event.IsEnabled,
            @event.OccurredAt,
            ct
        );

    public Task HandleAsync(RewardRemovedEvent @event, CancellationToken ct = default) =>
        BroadcastAsync(
            @event.BroadcasterId,
            "removed",
            @event.TwitchRewardId,
            @event.Title,
            cost: null,
            isEnabled: false,
            @event.OccurredAt,
            ct
        );

    private Task BroadcastAsync(
        Guid broadcasterId,
        string action,
        string rewardId,
        string title,
        int? cost,
        bool? isEnabled,
        DateTimeOffset occurredAt,
        CancellationToken ct
    )
    {
        if (broadcasterId == Guid.Empty)
            return Task.CompletedTask;

        RewardChangedDto dto = new(
            BroadcasterId: broadcasterId.ToString(),
            Action: action,
            RewardId: rewardId,
            Title: title,
            Cost: cost,
            IsEnabled: isEnabled,
            Timestamp: occurredAt.ToString("O")
        );

        return _notifier.SendRewardChangedAsync(broadcasterId.ToString(), dto, ct);
    }
}
