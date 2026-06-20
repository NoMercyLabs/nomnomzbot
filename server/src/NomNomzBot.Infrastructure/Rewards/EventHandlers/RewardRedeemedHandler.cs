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
using NomNomzBot.Domain.Platform.Entities;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Domain.Rewards.Entities;
using NomNomzBot.Domain.Rewards.Events;

namespace NomNomzBot.Infrastructure.Rewards.EventHandlers;

/// <summary>
/// Handles channel point reward redemptions.
/// Looks up the Reward entity by its Twitch reward ID and executes the
/// configured PipelineJson. If the reward has a simple Response text,
/// that is used directly. Falls back to the generic "reward_redeemed"
/// event_response Record if no specific reward pipeline is configured.
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

        string? pipelineJson = reward?.PipelineJson;

        // Fall back to simple Response text as a send_message pipeline
        if (pipelineJson is null && reward?.Response is not null)
        {
            pipelineJson = BuildResponsePipeline(reward.Response);
        }

        // Fall back to generic event_response:reward_redeemed record
        if (pipelineJson is null)
        {
            Record? genericConfig = await db.Records.FirstOrDefaultAsync(
                r =>
                    r.BroadcasterId == broadcasterId
                    && r.RecordType == "event_response:reward_redeemed",
                cancellationToken
            );
            pipelineJson = genericConfig?.Data;
        }

        if (string.IsNullOrWhiteSpace(pipelineJson))
            return;

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
