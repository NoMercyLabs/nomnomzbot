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
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Commands.Services;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Rewards.Dtos;
using NomNomzBot.Application.Rewards.Services;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Domain.Rewards.Entities;
using NomNomzBot.Domain.Rewards.Events;

namespace NomNomzBot.Infrastructure.Rewards.EventHandlers;

/// <summary>
/// Handles channel point reward redemptions.
/// Looks up the Reward entity by its Twitch reward ID and executes the
/// configured PipelineJson. If the reward has a simple Response text,
/// that is used directly. With no reward-specific behavior, the generic
/// <c>channel.channel_points_custom_reward_redemption.add</c> event response
/// runs through the shared executor.
/// </summary>
public sealed class RewardRedeemedHandler : IEventHandler<RewardRedeemedEvent>
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IPipelineEngine _pipeline;
    private readonly ILogger<RewardRedeemedHandler> _logger;

    public RewardRedeemedHandler(
        IServiceScopeFactory scopeFactory,
        IPipelineEngine pipeline,
        ILogger<RewardRedeemedHandler> logger
    )
    {
        _scopeFactory = scopeFactory;
        _pipeline = pipeline;
        _logger = logger;
    }

    public async Task HandleAsync(
        RewardRedeemedEvent @event,
        CancellationToken cancellationToken = default
    )
    {
        Guid broadcasterId = @event.BroadcasterId;
        if (broadcasterId == Guid.Empty)
            return;

        using IServiceScope scope = _scopeFactory.CreateScope();
        IApplicationDbContext db =
            scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

        Dictionary<string, string> variables = new(StringComparer.OrdinalIgnoreCase)
        {
            ["user"] = @event.UserDisplayName,
            ["user.id"] = @event.UserId,
            ["reward"] = @event.RewardTitle,
            ["reward.id"] = @event.RewardId,
            ["redemption.id"] = @event.RedemptionId,
            ["cost"] = @event.Cost.ToString(),
            ["input"] = @event.UserInput ?? string.Empty,
        };

        // Look up Reward entity matched by TwitchRewardId
        Reward? reward = await db.Rewards.FirstOrDefaultAsync(
            r => r.BroadcasterId == broadcasterId && r.TwitchRewardId == @event.RewardId,
            cancellationToken
        );

        // A time-limited reward starts its countdown the moment it's redeemed (idempotent per
        // redemption — an EventSub redelivery returns the existing timer). Orthogonal to the response.
        if (reward?.TimerDurationSeconds is int timerSeconds and > 0)
        {
            IRedemptionTimerService timers =
                scope.ServiceProvider.GetRequiredService<IRedemptionTimerService>();
            Result<RedemptionTimerDto> started = await timers.StartAsync(
                broadcasterId,
                @event.RedemptionId,
                @event.RewardId,
                @event.RewardTitle,
                @event.UserDisplayName,
                timerSeconds,
                cancellationToken
            );
            if (started.IsFailure)
                _logger.LogWarning(
                    "Redemption timer failed to start for {RedemptionId} in {Channel}: {Error}",
                    @event.RedemptionId,
                    broadcasterId,
                    started.ErrorMessage
                );
        }

        string? pipelineJson = reward?.PipelineJson;

        // A reward can bind a SAVED pipeline (the reward analogue of a timer's PipelineId): load its compiled
        // graph and run that — the path a reward-triggered play_sound takes. Takes precedence over the inline
        // PipelineJson / Response fallbacks. A binding whose graph is missing/deleted degrades to those.
        if (reward?.PipelineId is Guid boundPipelineId)
        {
            string? graphJson = await db
                .Pipelines.Where(p =>
                    p.Id == boundPipelineId
                    && p.BroadcasterId == broadcasterId
                    && p.DeletedAt == null
                )
                .Select(p => p.GraphJsonCache)
                .FirstOrDefaultAsync(cancellationToken);
            if (!string.IsNullOrEmpty(graphJson))
                pipelineJson = graphJson;
            else
                _logger.LogWarning(
                    "Reward {RewardId} in {Channel} binds pipeline {PipelineId} with no executable graph — falling back",
                    @event.RewardId,
                    broadcasterId,
                    boundPipelineId
                );
        }

        // Fall back to simple Response text as a send_message pipeline
        if (pipelineJson is null && reward?.Response is not null)
        {
            pipelineJson = BuildResponsePipeline(reward.Response);
        }

        // No reward-specific behavior → the generic redemption event response (the row the
        // event-responses page edits), through the shared executor like every other trigger source.
        if (string.IsNullOrWhiteSpace(pipelineJson))
        {
            IEventResponseExecutor executor =
                scope.ServiceProvider.GetRequiredService<IEventResponseExecutor>();
            await executor.ExecuteAsync(
                broadcasterId,
                "channel.channel_points_custom_reward_redemption.add",
                @event.UserId,
                @event.UserDisplayName,
                variables,
                cancellationToken
            );
            return;
        }

        _logger.LogDebug(
            "Executing pipeline for reward {RewardId} in channel {Channel}",
            @event.RewardId,
            broadcasterId
        );

        await ExecutePipelineAsync(
            broadcasterId,
            pipelineJson,
            @event.UserId,
            @event.UserDisplayName,
            @event.RedemptionId,
            @event.RewardId,
            variables,
            cancellationToken
        );
    }

    private static string BuildResponsePipeline(string message)
    {
        string escaped = message.Replace("\\", "\\\\").Replace("\"", "\\\"");
        return "{\"steps\":[{\"action\":{\"type\":\"send_message\",\"message\":\""
            + escaped
            + "\",\"target\":\"channel\"}}]}";
    }

    private async Task ExecutePipelineAsync(
        Guid broadcasterId,
        string pipelineJson,
        string userId,
        string displayName,
        string redemptionId,
        string rewardId,
        Dictionary<string, string> variables,
        CancellationToken ct
    )
    {
        try
        {
            await _pipeline.ExecuteAsync(
                new()
                {
                    BroadcasterId = broadcasterId,
                    PipelineJson = pipelineJson,
                    TriggeredByUserId = userId,
                    TriggeredByDisplayName = displayName,
                    RedemptionId = redemptionId,
                    RewardId = rewardId,
                    RawMessage = string.Empty,
                    InitialVariables = variables,
                },
                ct
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to execute reward pipeline in channel {Channel}",
                broadcasterId
            );
        }
    }
}
