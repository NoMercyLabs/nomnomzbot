// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Domain.Rewards.Entities;
using NomNomzBot.Domain.Rewards.Events;

namespace NomNomzBot.Infrastructure.Rewards.EventHandlers;

/// <summary>
/// Keeps local Reward records in sync with Twitch-side reward lifecycle events.
/// Creates/updates/removes local Reward entities when Twitch fires reward lifecycle events.
/// </summary>
public sealed class RewardLifecycleHandler
    : IEventHandler<RewardCreatedEvent>,
        IEventHandler<RewardUpdatedEvent>,
        IEventHandler<RewardRemovedEvent>
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<RewardLifecycleHandler> _logger;

    public RewardLifecycleHandler(
        IServiceScopeFactory scopeFactory,
        ILogger<RewardLifecycleHandler> logger
    )
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task HandleAsync(RewardCreatedEvent @event, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(@event.BroadcasterId))
            return;

        using IServiceScope scope = _scopeFactory.CreateScope();
        IApplicationDbContext db =
            scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

        Reward? existing = await db.Rewards.FirstOrDefaultAsync(
            r =>
                r.BroadcasterId == @event.BroadcasterId
                && r.TwitchRewardId == @event.TwitchRewardId,
            ct
        );

        if (existing is not null)
            return; // already tracked

        db.Rewards.Add(
            new()
            {
                Id = Guid.NewGuid(),
                BroadcasterId = @event.BroadcasterId,
                TwitchRewardId = @event.TwitchRewardId,
                Title = @event.Title,
                Cost = @event.Cost,
                IsEnabled = @event.IsEnabled,
                IsPlatform = true,
            }
        );
        await db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Reward created on Twitch: '{Title}' ({TwitchRewardId}) for {BroadcasterId}",
            @event.Title,
            @event.TwitchRewardId,
            @event.BroadcasterId
        );
    }

    public async Task HandleAsync(RewardUpdatedEvent @event, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(@event.BroadcasterId))
            return;

        using IServiceScope scope = _scopeFactory.CreateScope();
        IApplicationDbContext db =
            scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

        Reward? reward = await db.Rewards.FirstOrDefaultAsync(
            r =>
                r.BroadcasterId == @event.BroadcasterId
                && r.TwitchRewardId == @event.TwitchRewardId,
            ct
        );

        if (reward is null)
            return;

        reward.Title = @event.Title;
        reward.Cost = @event.Cost;
        reward.IsEnabled = @event.IsEnabled;
        await db.SaveChangesAsync(ct);
    }

    public async Task HandleAsync(RewardRemovedEvent @event, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(@event.BroadcasterId))
            return;

        using IServiceScope scope = _scopeFactory.CreateScope();
        IApplicationDbContext db =
            scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

        Reward? reward = await db.Rewards.FirstOrDefaultAsync(
            r =>
                r.BroadcasterId == @event.BroadcasterId
                && r.TwitchRewardId == @event.TwitchRewardId,
            ct
        );

        if (reward is null)
            return;

        // Soft-delete — keep config (PipelineJson) for potential re-creation
        reward.IsEnabled = false;
        reward.TwitchRewardId = null;
        await db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Reward removed from Twitch: '{Title}' for {BroadcasterId}",
            @event.Title,
            @event.BroadcasterId
        );
    }
}
