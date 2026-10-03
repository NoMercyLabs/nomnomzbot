// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NomNomzBot.Application.Abstractions.Auth;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.CustomCode;
using NomNomzBot.Infrastructure.Commands.Jobs;
using NomNomzBot.Infrastructure.DevPlatform;
using NomNomzBot.Infrastructure.Rewards.EventHandlers;
using NomNomzBot.Infrastructure.Webhooks.EventHandlers;

namespace NomNomzBot.Infrastructure.CustomCode;

/// <summary>
/// Resolves the SDK trigger keys of a script: script -> <c>run_code</c> steps -> pipelines -> the chat command,
/// timer, reward, webhook endpoint, event response and pipeline trigger rows that start each pipeline. Every query is scoped to the current
/// channel, so another channel's pipeline never adds a key.
/// </summary>
public sealed class CodeScriptTriggerResolver(
    IApplicationDbContext db,
    ICurrentTenantService tenant
) : ICodeScriptTriggerResolver
{
    private const string EventKind = "event";
    private const string CommandKind = "command";

    public async Task<Result<IReadOnlyList<string>>> GetTriggerKeysAsync(
        Guid codeScriptId,
        CancellationToken cancellationToken = default
    )
    {
        if (tenant.BroadcasterId is not { } broadcasterId)
            return Result.Failure<IReadOnlyList<string>>("No tenant.", "NO_TENANT");

        bool exists = await db.CodeScripts.AnyAsync(
            script =>
                script.Id == codeScriptId
                && script.BroadcasterId == broadcasterId
                && script.DeletedAt == null,
            cancellationToken
        );
        if (!exists)
            return Result.Failure<IReadOnlyList<string>>("Script not found.", "NOT_FOUND");

        List<Guid> pipelineIds = await db
            .PipelineSteps.Where(step =>
                step.BroadcasterId == broadcasterId && step.CodeScriptId == codeScriptId
            )
            .Select(step => step.PipelineId)
            .Distinct()
            .ToListAsync(cancellationToken);
        if (pipelineIds.Count == 0)
            return Result.Success<IReadOnlyList<string>>([]);

        bool hasCommand = await db.Commands.AnyAsync(
            command =>
                command.BroadcasterId == broadcasterId
                && command.IsEnabled
                && command.PipelineId != null
                && pipelineIds.Contains(command.PipelineId.Value),
            cancellationToken
        );

        List<string> eventTypes = await db
            .EventResponses.Where(response =>
                response.BroadcasterId == broadcasterId
                && response.IsEnabled
                && response.PipelineId != null
                && pipelineIds.Contains(response.PipelineId.Value)
            )
            .Select(response => response.EventType)
            .ToListAsync(cancellationToken);

        bool hasTimer = await db.Timers.AnyAsync(
            timer =>
                timer.BroadcasterId == broadcasterId
                && timer.IsEnabled
                && timer.PipelineId != null
                && pipelineIds.Contains(timer.PipelineId.Value),
            cancellationToken
        );

        bool hasReward = await db.Rewards.AnyAsync(
            reward =>
                reward.BroadcasterId == broadcasterId
                && reward.IsEnabled
                && reward.PipelineId != null
                && pipelineIds.Contains(reward.PipelineId.Value),
            cancellationToken
        );

        bool hasWebhook = await db.InboundWebhookEndpoints.AnyAsync(
            endpoint =>
                endpoint.BroadcasterId == broadcasterId
                && endpoint.IsEnabled
                && endpoint.TargetPipelineId != null
                && pipelineIds.Contains(endpoint.TargetPipelineId.Value),
            cancellationToken
        );

        List<(string Kind, string ConfigJson)> triggerRows = (
            await db
                .PipelineTriggers.Where(trigger =>
                    trigger.BroadcasterId == broadcasterId
                    && trigger.IsEnabled
                    && pipelineIds.Contains(trigger.PipelineId)
                )
                .Select(trigger => new { trigger.Kind, trigger.ConfigJson })
                .ToListAsync(cancellationToken)
        )
            .Select(row => (row.Kind, row.ConfigJson))
            .ToList();

        HashSet<string> keys = new(StringComparer.Ordinal);
        if (hasCommand)
            keys.Add(ChatCommandVariableKeys.Trigger);
        if (hasTimer)
            keys.Add(TimerSampleSource.ResponseKey);
        if (hasReward)
            keys.Add(RewardRedeemedSampleSource.ResponseKey);
        if (hasWebhook)
            keys.Add(InboundWebhookTriggerSampleSource.ResponseKey);
        keys.UnionWith(eventTypes);
        foreach ((string kind, string configJson) in triggerRows)
        {
            if (kind == CommandKind)
                keys.Add(ChatCommandVariableKeys.Trigger);
            else if (kind == EventKind && ReadEventType(configJson) is { } eventType)
                keys.Add(eventType);
        }

        return Result.Success<IReadOnlyList<string>>([
            .. keys.OrderBy(key => key, StringComparer.Ordinal),
        ]);
    }

    private static string? ReadEventType(string configJson)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(configJson);
            return
                document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("EventType", out JsonElement value)
                && value.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(value.GetString())
                ? value.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
