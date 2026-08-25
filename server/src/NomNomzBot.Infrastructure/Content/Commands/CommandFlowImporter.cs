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
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Commands.Import;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Domain.Commands.Entities;
using NomNomzBot.Domain.PickLists.Entities;

namespace NomNomzBot.Infrastructure.Content.Commands;

/// <summary>
/// Turns a <see cref="CommandFlowSpec"/> into a real, editable pipeline built only from generic blocks:
/// each pool becomes a channel pick list, and the branch rules become a chain of <c>if</c> blocks whose arms
/// are <c>pick_from_list</c> → <c>send_message {{pick}}</c>. Nothing about the resulting command is special —
/// it is exactly what the streamer would have assembled by hand, so every step can be reordered, retimed,
/// rewritten or deleted in the editor afterwards.
/// <para>
/// It fills EMPTY commands only. A command whose pipeline already has steps is the streamer's own work and is
/// left untouched; a command with no pipeline at all gets one. (A channel full of named commands whose
/// pipelines had zero steps — commands that matched, ran, and did nothing — is exactly the state this exists
/// to repair.)
/// </para>
/// </summary>
public sealed class CommandFlowImporter
{
    private readonly IApplicationDbContext _db;

    public CommandFlowImporter(IApplicationDbContext db)
    {
        _db = db;
    }

    /// <summary>
    /// Imports every spec for one channel. Returns the names actually filled in — a spec whose command already
    /// has a built pipeline is reported as skipped rather than silently ignored.
    /// </summary>
    public async Task<Result<CommandFlowImportReport>> ImportAsync(
        Guid broadcasterId,
        IReadOnlyList<CommandFlowSpec> specs,
        CancellationToken ct = default
    )
    {
        if (specs.Count == 0)
            return Result.Failure<CommandFlowImportReport>(
                "No command flows to import.",
                "EMPTY_IMPORT"
            );

        List<string> names =
        [
            .. specs.Select(s => s.Command.Trim().ToLowerInvariant()).Where(n => n.Length > 0),
        ];

        List<Command> existing = await _db
            .Commands.Where(c =>
                c.BroadcasterId == broadcasterId && names.Contains(c.NameNormalized)
            )
            .ToListAsync(ct);

        HashSet<Guid> pipelinesWithSteps =
        [
            .. await _db
                .PipelineSteps.Where(s => s.BroadcasterId == broadcasterId)
                .Select(s => s.PipelineId)
                .Distinct()
                .ToListAsync(ct),
        ];

        Dictionary<string, PickList> lists = await _db
            .PickLists.Where(l => l.BroadcasterId == broadcasterId)
            .ToDictionaryAsync(l => l.Name, ct);

        List<string> filled = [];
        List<string> skipped = [];

        foreach (CommandFlowSpec spec in specs)
        {
            string name = spec.Command.Trim().ToLowerInvariant();
            if (name.Length == 0)
                continue;

            Result validation = Validate(spec);
            if (validation.IsFailure)
                return Result.Failure<CommandFlowImportReport>(
                    $"{name}: {validation.ErrorMessage}",
                    validation.ErrorCode
                );

            Command? command = existing.FirstOrDefault(c => c.NameNormalized == name);
            if (
                command?.PipelineId is { } existingPipelineId
                && pipelinesWithSteps.Contains(existingPipelineId)
            )
            {
                skipped.Add(name);
                continue;
            }

            UpsertPools(broadcasterId, spec, lists);

            Pipeline pipeline = await ResolvePipelineAsync(broadcasterId, spec, command, ct);
            BuildSteps(broadcasterId, spec, pipeline);
            AttachCommand(broadcasterId, spec, command, pipeline);

            filled.Add(name);
        }

        await _db.SaveChangesAsync(ct);
        return Result.Success(new CommandFlowImportReport(filled, skipped));
    }

    private static Result Validate(CommandFlowSpec spec)
    {
        if (spec.Branches.Count == 0)
            return Result.Failure("the spec has no branches.", "NO_BRANCHES");

        if (spec.Branches[^1].Condition is not null)
            return Result.Failure(
                "the last branch must be the unconditional fallback, or a caller can hit a "
                    + "command that matches and answers nothing.",
                "NO_FALLBACK"
            );

        if (spec.Branches.Take(spec.Branches.Count - 1).Any(b => b.Condition is null))
            return Result.Failure(
                "only the last branch may be unconditional — an earlier one would shadow the rest.",
                "UNREACHABLE_BRANCH"
            );

        foreach (CommandFlowBranch branch in spec.Branches)
        {
            if (!spec.Pools.TryGetValue(branch.Pool, out IReadOnlyList<string>? lines))
                return Result.Failure(
                    $"branch references pool '{branch.Pool}', which the spec does not define.",
                    "UNKNOWN_POOL"
                );

            if (lines.Count == 0)
                return Result.Failure(
                    $"pool '{branch.Pool}' is empty, so that branch would answer nothing.",
                    "EMPTY_POOL"
                );
        }

        return Result.Success();
    }

    private void UpsertPools(
        Guid broadcasterId,
        CommandFlowSpec spec,
        Dictionary<string, PickList> lists
    )
    {
        foreach ((string pool, IReadOnlyList<string> lines) in spec.Pools)
        {
            string listName = ListName(spec.Command, pool);
            if (lists.TryGetValue(listName, out PickList? found))
            {
                found.Items = [.. lines];
                continue;
            }

            PickList created = new()
            {
                Id = Guid.CreateVersion7(),
                BroadcasterId = broadcasterId,
                Name = listName,
                Description = $"Lines !{spec.Command} answers with ({pool}).",
                Items = [.. lines],
            };
            _db.PickLists.Add(created);
            lists[listName] = created;
        }
    }

    private async Task<Pipeline> ResolvePipelineAsync(
        Guid broadcasterId,
        CommandFlowSpec spec,
        Command? command,
        CancellationToken ct
    )
    {
        if (command?.PipelineId is { } pipelineId)
        {
            Pipeline? found = await _db.Pipelines.FirstOrDefaultAsync(
                p => p.Id == pipelineId && p.BroadcasterId == broadcasterId,
                ct
            );
            if (found is not null)
                return found;
        }

        Pipeline created = new()
        {
            Id = Guid.CreateVersion7(),
            BroadcasterId = broadcasterId,
            Name = spec.Command,
            Description = spec.Description,
            TriggerKind = "command",
            IsEnabled = true,
        };
        _db.Pipelines.Add(created);
        return created;
    }

    /// <summary>
    /// Materializes the branch rules as nested <c>if</c> blocks: rule N's else-arm holds rule N+1, and the
    /// final fallback sits in the innermost else. That is the same shape the editor produces for
    /// if / else-if / else, so the imported flow reads as an ordinary tree rather than a special case.
    /// </summary>
    private void BuildSteps(Guid broadcasterId, CommandFlowSpec spec, Pipeline pipeline)
    {
        int order = 0;
        Guid? parentId = null;
        string? branchLane = null;

        for (int i = 0; i < spec.Branches.Count; i++)
        {
            CommandFlowBranch branch = spec.Branches[i];

            if (branch.Condition is null)
            {
                AddAnswer(
                    broadcasterId,
                    pipeline,
                    branch.Pool,
                    spec.Command,
                    parentId,
                    branchLane,
                    ref order
                );
                break;
            }

            PipelineStep ifStep = new()
            {
                Id = Guid.CreateVersion7(),
                PipelineId = pipeline.Id,
                BroadcasterId = broadcasterId,
                ParentStepId = parentId,
                Branch = branchLane,
                BlockKind = "if",
                BlockConfigJson = "{}",
                ActionType = "comparison",
                ConfigJson = "{}",
                Order = order++,
                IsEnabled = true,
            };
            _db.PipelineSteps.Add(ifStep);
            _db.PipelineStepConditions.Add(
                new()
                {
                    Id = Guid.CreateVersion7(),
                    PipelineStepId = ifStep.Id,
                    BroadcasterId = broadcasterId,
                    ConditionType = "comparison",
                    LeftOperand = branch.Condition.Left,
                    Operator = branch.Condition.Operator,
                    RightOperand = branch.Condition.Right,
                    Order = 0,
                }
            );

            AddAnswer(
                broadcasterId,
                pipeline,
                branch.Pool,
                spec.Command,
                ifStep.Id,
                "then",
                ref order
            );

            parentId = ifStep.Id;
            branchLane = "else";
        }
    }

    /// <summary>One answer: roll a line from the pool, then say the rolled line. Two ordinary blocks.</summary>
    private void AddAnswer(
        Guid broadcasterId,
        Pipeline pipeline,
        string pool,
        string command,
        Guid? parentId,
        string? branchLane,
        ref int order
    )
    {
        _db.PipelineSteps.Add(
            new()
            {
                Id = Guid.CreateVersion7(),
                PipelineId = pipeline.Id,
                BroadcasterId = broadcasterId,
                ParentStepId = parentId,
                Branch = branchLane,
                ActionType = "pick_from_list",
                ConfigJson = $$"""{"list":"{{ListName(command, pool)}}","variable":"pick"}""",
                Order = order++,
                IsEnabled = true,
            }
        );
        _db.PipelineSteps.Add(
            new()
            {
                Id = Guid.CreateVersion7(),
                PipelineId = pipeline.Id,
                BroadcasterId = broadcasterId,
                ParentStepId = parentId,
                Branch = branchLane,
                ActionType = "send_message",
                ConfigJson = """{"message":"{{pick}}"}""",
                Order = order++,
                IsEnabled = true,
            }
        );
    }

    private void AttachCommand(
        Guid broadcasterId,
        CommandFlowSpec spec,
        Command? command,
        Pipeline pipeline
    )
    {
        if (command is not null)
        {
            // The streamer's own row keeps its name, wording and permission — it only gains the flow it
            // was always supposed to run.
            command.PipelineId = pipeline.Id;
            command.Tier = "pipeline";
            command.IsEnabled = true;
            return;
        }

        string name = spec.Command.Trim().ToLowerInvariant();
        _db.Commands.Add(
            new()
            {
                Id = Guid.CreateVersion7(),
                BroadcasterId = broadcasterId,
                Name = name,
                NameNormalized = name,
                Description = spec.Description,
                Tier = "pipeline",
                PipelineId = pipeline.Id,
                IsEnabled = true,
            }
        );
    }

    private static string ListName(string command, string pool) =>
        $"{command.Trim().ToLowerInvariant()}.{pool.Trim().ToLowerInvariant()}";
}

/// <summary>What an import actually did — filled commands and ones left alone because they were already built.</summary>
public sealed record CommandFlowImportReport(
    IReadOnlyList<string> Filled,
    IReadOnlyList<string> Skipped
);
