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
using NomNomzBot.Application.Commands.Services;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Domain.Rewards.Entities;
using NomNomzBot.Domain.Rewards.Events;

namespace NomNomzBot.Infrastructure.Rewards.EventHandlers;

/// <summary>
/// Keeps local Reward records in sync with Twitch-side reward lifecycle events.
/// Creates/updates/removes local Reward entities when Twitch fires reward lifecycle events.
/// The update leg ALSO doubles as the reward-state trigger source: it holds both the last-known local state
/// and the incoming Twitch state in one hand, so it derives the pause/enable transitions right there
/// (<c>reward.paused</c>/<c>reward.resumed</c>/<c>reward.enabled</c>/<c>reward.disabled</c>) and dispatches
/// each through <see cref="IEventResponseExecutor"/> AFTER persisting the sync — no handler-ordering race,
/// no second read of a row another handler may have already overwritten.
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
        if (@event.BroadcasterId == Guid.Empty)
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
                IsPaused = @event.IsPaused,
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
        if (@event.BroadcasterId == Guid.Empty)
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

        // Capture the last-known state BEFORE syncing — the transition is old-vs-new, held in one hand.
        bool wasEnabled = reward.IsEnabled;
        bool wasPaused = reward.IsPaused;

        reward.Title = @event.Title;
        reward.Cost = @event.Cost;
        reward.IsEnabled = @event.IsEnabled;
        reward.IsPaused = @event.IsPaused;
        await db.SaveChangesAsync(ct);

        // State transitions → the opt-in reward.* event responses (legacy parity: the reward change-handler
        // announcements). Only actual flips fire; a title/cost-only update dispatches nothing.
        IEventResponseExecutor executor =
            scope.ServiceProvider.GetRequiredService<IEventResponseExecutor>();
        Dictionary<string, string> variables = new(StringComparer.OrdinalIgnoreCase)
        {
            ["reward"] = @event.Title,
            ["reward.id"] = @event.TwitchRewardId,
            ["cost"] = @event.Cost.ToString(),
        };
        if (wasPaused != @event.IsPaused)
            await executor.ExecuteAsync(
                @event.BroadcasterId,
                @event.IsPaused ? "reward.paused" : "reward.resumed",
                userId: null,
                userDisplayName: null,
                variables,
                ct
            );
        if (wasEnabled != @event.IsEnabled)
            await executor.ExecuteAsync(
                @event.BroadcasterId,
                @event.IsEnabled ? "reward.enabled" : "reward.disabled",
                userId: null,
                userDisplayName: null,
                variables,
                ct
            );
    }

    public async Task HandleAsync(RewardRemovedEvent @event, CancellationToken ct = default)
    {
        if (@event.BroadcasterId == Guid.Empty)
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
