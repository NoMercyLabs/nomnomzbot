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

using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Domain.Commands.Entities;
using NomNomzBot.Domain.Commands.Enums;
using NomNomzBot.Domain.Commands.ValueObjects;
using NomNomzBot.Domain.Platform;
using NomNomzBot.Domain.Platform.Entities;
using NomNomzBot.Infrastructure.AutomationApi;
using NomNomzBot.Infrastructure.Content.PlatformContent;

namespace NomNomzBot.Infrastructure.Commands;

/// <summary>
/// Spec commands-pipelines 12.6: a surface row bound to a wrapper pipeline (one untouched step, no triggers,
/// no other reference, owned by exactly one command of the same channel) runs that command directly. Any
/// other bound pipeline is kept and reported with its reason. A pipeline row is never deleted.
/// </summary>
public sealed class WrapperPipelineMigrator
{
    /// <summary>The global <see cref="Configuration"/> key that records this data set as migrated.</summary>
    public const string MarkerKey = "system:migration:wrapper-pipelines:v1";

    private readonly IApplicationDbContext _db;

    public WrapperPipelineMigrator(IApplicationDbContext db) => _db = db;

    /// <summary>
    /// Rebinds every safe wrapper binding on the seven surfaces and saves once. Runs once per data set: the
    /// pass ends by writing the global <see cref="MarkerKey"/> row in the same save, and a later call returns an
    /// empty report without reading a binding. A user who picks a wrapper pipeline as a target afterwards (or
    /// switches a retired Stream Deck wrapper back on) keeps that choice at every later boot.
    /// </summary>
    public async Task<WrapperMigrationReport> RunAsync(CancellationToken ct = default)
    {
        if (
            await _db.Configurations.AnyAsync(
                c => c.BroadcasterId == null && c.Key == MarkerKey,
                ct
            )
        )
            return new([], [], []);

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

        List<KeptWrapper> kept =
        [
            .. decisions
                .Where(d => d.Value.CommandId is null)
                .Select(d => new KeptWrapper(d.Key.PipelineId, d.Value.KeptReason!)),
        ];
        List<RetiredStreamDeckWrapper> retired = await RetireStreamDeckWrappersAsync(
            decisions.Keys.Select(k => k.PipelineId).ToHashSet(),
            kept,
            ct
        );

        _db.Configurations.Add(
            new()
            {
                BroadcasterId = null,
                Key = MarkerKey,
                Value = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
            }
        );
        await _db.SaveChangesAsync(ct);

        return new(rebound, kept, retired);
    }

    /// <summary>
    /// A Stream Deck key names a pipeline (no surface row holds the binding). The old pairing flow made one
    /// single-action pipeline per action type, named by it. When such a pipeline is untouched (name equals the
    /// action type, one step of that type, only placeholder parameters, no triggers, not referenced) it is
    /// switched off: the by-name lookup only sees enabled pipelines, so the key then runs the action target.
    /// </summary>
    private async Task<List<RetiredStreamDeckWrapper>> RetireStreamDeckWrappersAsync(
        HashSet<Guid> surfaceBound,
        List<KeptWrapper> kept,
        CancellationToken ct
    )
    {
        List<RetiredStreamDeckWrapper> retired = [];
        List<Pipeline> candidates = await _db
            .Pipelines.Where(p =>
                p.IsEnabled && (p.Name.StartsWith("music_") || p.Name.StartsWith("obs_"))
            )
            .ToListAsync(ct);

        foreach (Pipeline pipeline in candidates)
        {
            if (surfaceBound.Contains(pipeline.Id))
                continue;
            if (!StreamDeckActions.IsStreamDeckAction(pipeline.Name))
                continue;
            if (!IsAutoMadeKeyWrapper(pipeline))
            {
                kept.Add(
                    new(pipeline.Id, "the pipeline is not an untouched Stream Deck key wrapper")
                );
                continue;
            }

            string idText = pipeline.Id.ToString();
            if (await _db.PipelineTriggers.AnyAsync(t => t.PipelineId == pipeline.Id, ct))
            {
                kept.Add(new(pipeline.Id, "the pipeline has its own triggers"));
                continue;
            }
            if (
                await _db.PipelineSteps.AnyAsync(
                    s => s.PipelineId != pipeline.Id && s.ConfigJson.Contains(idText),
                    ct
                )
            )
            {
                kept.Add(new(pipeline.Id, "another pipeline step references it"));
                continue;
            }

            pipeline.IsEnabled = false;
            retired.Add(new(pipeline.Id, pipeline.Name));
        }

        return retired;
    }

    private static bool IsAutoMadeKeyWrapper(Pipeline pipeline)
    {
        if (string.IsNullOrWhiteSpace(pipeline.GraphJsonCache))
            return false;

        try
        {
            using JsonDocument document = JsonDocument.Parse(pipeline.GraphJsonCache);
            if (
                !document.RootElement.TryGetProperty("steps", out JsonElement steps)
                || steps.ValueKind != JsonValueKind.Array
                || steps.GetArrayLength() != 1
                || !steps[0].TryGetProperty("action", out JsonElement action)
                || action.GetProperty("type").GetString() != pipeline.Name
            )
                return false;

            return !action.TryGetProperty("parameters", out JsonElement parameters)
                || parameters.EnumerateObject().All(p => IsPlaceholder(p.Value));
        }
        catch (Exception ex)
            when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return false;
        }
    }

    private static bool IsPlaceholder(JsonElement value) =>
        value.ValueKind == JsonValueKind.String
        && value.GetString() is { Length: > 2 } text
        && text[0] == '{'
        && text[^1] == '}';

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
