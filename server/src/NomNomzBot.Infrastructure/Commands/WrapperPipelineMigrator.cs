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
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Domain.Commands.Entities;
using NomNomzBot.Domain.Commands.Enums;
using NomNomzBot.Domain.Commands.ValueObjects;
using NomNomzBot.Domain.Platform;
using NomNomzBot.Infrastructure.Content.PlatformContent;

namespace NomNomzBot.Infrastructure.Commands;

/// <summary>
/// Spec commands-pipelines 12.6: a surface row bound to a wrapper pipeline (one untouched step, no triggers,
/// no other reference, owned by exactly one command of the same channel) runs that command directly. Any
/// other bound pipeline is kept and reported with its reason. A pipeline row is never deleted.
/// </summary>
public sealed class WrapperPipelineMigrator
{
    private readonly IApplicationDbContext _db;

    public WrapperPipelineMigrator(IApplicationDbContext db) => _db = db;

    /// <summary>Rebinds every safe wrapper binding on the seven surfaces and saves once. Idempotent.</summary>
    public async Task<WrapperMigrationReport> RunAsync(CancellationToken ct = default)
    {
        List<ReboundBinding> rebound = [];
        Dictionary<(Guid PipelineId, Guid ChannelId), WrapperDecision> decisions = [];

        await RebindAsync(_db.Rewards, "Rewards", r => r.Id, rebound, decisions, ct);
        await RebindAsync(_db.Timers, "Timers", r => r.Id, rebound, decisions, ct);
        await RebindAsync(_db.EventResponses, "EventResponses", r => r.Id, rebound, decisions, ct);
        await RebindAsync(_db.ChatTriggers, "ChatTriggers", r => r.Id, rebound, decisions, ct);
        await RebindAsync(
            _db.ScheduledPipelineTasks,
            "ScheduledPipelineTasks",
            r => r.Id,
            rebound,
            decisions,
            ct
        );
        await RebindAsync(_db.Giveaways, "Giveaways", r => r.Id, rebound, decisions, ct);
        await RebindAsync(_db.CatalogItems, "CatalogItems", r => r.Id, rebound, decisions, ct);

        if (rebound.Count > 0)
            await _db.SaveChangesAsync(ct);

        List<KeptWrapper> kept =
        [
            .. decisions
                .Where(d => d.Value.CommandId is null)
                .Select(d => new KeptWrapper(d.Key.PipelineId, d.Value.KeptReason!)),
        ];
        return new(rebound, kept);
    }

    private async Task RebindAsync<T>(
        IQueryable<T> rows,
        string surface,
        Func<T, Guid> rowId,
        List<ReboundBinding> rebound,
        Dictionary<(Guid PipelineId, Guid ChannelId), WrapperDecision> decisions,
        CancellationToken ct
    )
        where T : class, IBindingTargetHolder, ITenantScoped
    {
        List<T> bound = await rows.Where(r => r.TargetKind == BindingTargetKind.Pipeline)
            .ToListAsync(ct);

        foreach (T row in bound)
        {
            if (row.LegacyPipelineId is not { } pipelineId)
                continue;

            WrapperDecision decision = await DecideAsync(
                pipelineId,
                row.BroadcasterId,
                decisions,
                ct
            );
            if (decision.CommandId is not { } commandId)
                continue;

            row.SetBindingTarget(
                new BindingTarget(BindingTargetKind.Command, commandId.ToString())
            );
            rebound.Add(new(surface, rowId(row), pipelineId, commandId));
        }
    }

    private async Task<WrapperDecision> DecideAsync(
        Guid pipelineId,
        Guid channelId,
        Dictionary<(Guid PipelineId, Guid ChannelId), WrapperDecision> decisions,
        CancellationToken ct
    )
    {
        if (decisions.TryGetValue((pipelineId, channelId), out WrapperDecision? known))
            return known;

        WrapperDecision decision = await EvaluateAsync(pipelineId, channelId, ct);
        decisions[(pipelineId, channelId)] = decision;
        return decision;
    }

    private async Task<WrapperDecision> EvaluateAsync(
        Guid pipelineId,
        Guid channelId,
        CancellationToken ct
    )
    {
        Pipeline? pipeline = await _db.Pipelines.FirstOrDefaultAsync(p => p.Id == pipelineId, ct);
        if (pipeline is null)
            return WrapperDecision.Keep("the pipeline row does not exist");
        if (pipeline.BroadcasterId != channelId)
            return WrapperDecision.Keep("the pipeline belongs to another channel");
        if (!HasExactlyOneStep(pipeline.GraphJsonCache))
            return WrapperDecision.Keep("the graph does not hold exactly one step");
        if (!IsUntouched(pipeline))
            return WrapperDecision.Keep("the pipeline was customized (hash mismatch)");

        if (await _db.PipelineTriggers.AnyAsync(t => t.PipelineId == pipelineId, ct))
            return WrapperDecision.Keep("the pipeline has its own triggers");

        string idText = pipelineId.ToString();
        bool referenced = await _db.PipelineSteps.AnyAsync(
            s => s.PipelineId != pipelineId && s.ConfigJson.Contains(idText),
            ct
        );
        if (referenced)
            return WrapperDecision.Keep("another pipeline step references it");

        List<Guid> owners = await _db
            .Commands.Where(c => c.BroadcasterId == channelId && c.PipelineId == pipelineId)
            .Select(c => c.Id)
            .Take(2)
            .ToListAsync(ct);
        return owners.Count == 1
            ? WrapperDecision.Rebind(owners[0])
            : WrapperDecision.Keep(
                owners.Count == 0
                    ? "no command of the channel owns the pipeline"
                    : "more than one command owns the pipeline"
            );
    }

    private static bool IsUntouched(Pipeline pipeline) =>
        pipeline.PlatformSourceHash is not null
        && pipeline.PlatformSourceHash == PlatformContentHash.ComputeHash(pipeline.GraphJsonCache);

    private static bool HasExactlyOneStep(string? graphJson)
    {
        if (string.IsNullOrWhiteSpace(graphJson))
            return false;

        try
        {
            using JsonDocument document = JsonDocument.Parse(graphJson);
            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("steps", out JsonElement steps)
                && steps.ValueKind == JsonValueKind.Array
                && steps.GetArrayLength() == 1;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private sealed record WrapperDecision(Guid? CommandId, string? KeptReason)
    {
        public static WrapperDecision Rebind(Guid commandId) => new(commandId, null);

        public static WrapperDecision Keep(string reason) => new(null, reason);
    }
}
